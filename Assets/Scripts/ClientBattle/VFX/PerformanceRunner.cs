using System;
using System.Collections;
using ClientBattle.Audio;
using ClientBattle.Events;
using ClientBattle.Test;
using ClientBattle.Units;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】PerformanceRunner（MonoBehaviour 单例）：播放控制器。
    //
    // 2026-07-23 架构重构（docs/client/architecture.md §二）后本类只剩：
    //   - 生命周期状态机（PlaybackState）与全部公开入口
    //     Play/Replay/Skip/Teardown/Highlight/Stop —— 迁移表见架构文档
    //   - 唯一协程宿主（主时间轴）
    //   - 硬停止 HardStop() 的单一实现（R-1.2）
    // 其余职责已拆出：
    //   建世界      → PlaybackWorldBuilder（产出 PlaybackSession）
    //   主循环/组分派 → PlaybackDirector
    //   cut-in 策略  → CutInPolicy
    //   落账        → EventApplyService（唯一镜像写入，R-7.4）
    // =========================================================================

    public class PerformanceRunner : MonoBehaviour, IPlaybackPacing
    {
        public static PerformanceRunner Instance { get; private set; }

        [Header("播放设置")]
        public float Speed = 1f;
        [Tooltip("全局时长倍率：动画节拍与单元/行动停顿一并放大（默认 2=放慢一倍看清战报）")]
        public float DurationMul = 2f;
        public PerformanceDatabase Database;

        [Header("节奏（呼吸间隙；常驻动画/待机呼吸/光环不受影响，继续播放）")]
        [Tooltip("每个英雄行动结束后的停顿秒数（应长于单元停顿；再乘 DurationMul）")]
        public float ActionPauseSeconds = 0.55f;
        [Tooltip("每个播放单元结束后的停顿秒数（再乘 DurationMul）")]
        public float GroupPauseSeconds = 0.35f;
        public event Action OnAllComplete;

        /// <summary>生命周期状态（R-1.1）；IsPlaying 为兼容旧调用方的视图。</summary>
        public PlaybackState State { get; private set; } = PlaybackState.Idle;
        public bool IsPlaying => State is PlaybackState.Building
                                       or PlaybackState.Prewarming
                                       or PlaybackState.Playing;

        // IPlaybackPacing（Director/Builder 只读实时节奏值）
        float IPlaybackPacing.Speed => Speed;
        float IPlaybackPacing.DurationMul => DurationMul;
        float IPlaybackPacing.ActionPauseSeconds => ActionPauseSeconds;
        float IPlaybackPacing.GroupPauseSeconds => GroupPauseSeconds;

        PlaybackSession _session;
        PlaybackDirector _director;
        BattleBoardView _board;

        public static PerformanceRunner Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("PerformanceRunner");
                Instance = go.AddComponent<PerformanceRunner>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ---------------------------------------------------------- 公开入口

        /// <summary>解析并从头播放一份战报 JSON。</summary>
        public void PlayBattleReport(string json)
        {
            var report = BattleReport.Parse(json);
            if (report == null)
            {
                Debug.LogError("[ClientBattle] 战报解析失败，无法播放");
                return;
            }
            PlayBattleReport(report);
        }

        public void PlayBattleReport(BattleReport report)
        {
            HardStop();
            BuildSession(report);
            StartCoroutine(PlayLoop());
        }

        /// <summary>跳到结尾：硬停止在飞演出，剩余事件按原始 seq 序静默落账（终态与
        /// 正常播完一致，R-1.4）。showSettlement=false 供回配阵等路径静默收尾。</summary>
        public void SkipToEnd(bool showSettlement = true)
        {
            if (!IsPlaying || _session == null) return;
            var session = _session;
            HardStop();
            foreach (var game in session.Report.Games)
                foreach (var ev in game.Events)
                    EventApplyService.Apply(ev, session.Ctx, animated: false);
            FinishPlayback(showSettlement);
        }

        /// <summary>停止播放（不拆世界）。任何入口的清残留都收口到 HardStop。</summary>
        public void StopPlayback() => HardStop();

        /// <summary>停播并拆除战场可视（棋盘/单位/背景）。配阵页「返回配阵」用；
        /// 下次 PlayBattleReport 会重建（R-1.5）。</summary>
        public void TeardownWorld()
        {
            HardStop(); // 已含 Banner.Clear
            SettlementPanel.Instance?.Hide();
            MomentumService.ClearAll();
            UnitAuraService.ClearAll();
            if (_board != null)
            {
                _board.Clear();
                Destroy(_board.gameObject);
                _board = null;
            }
            _session = null;
            State = PlaybackState.Idle;
        }

        /// <summary>终局高光回放：按 HighlightSelector 选出的最佳行动窗整段重播。
        /// 纯客户端二次剪辑：窗前事件静默落账、窗内事件正常演出（R-1.6）。</summary>
        public void PlayHighlight(string teamId = "A")
        {
            if (_session?.Report == null)
            {
                Debug.LogWarning("[ClientBattle] 无战报，先播放一场");
                return;
            }
            var report = _session.Report;
            if (!HighlightSelector.TryFindBestWindow(report, teamId, out var window))
            {
                Debug.LogWarning("[ClientBattle] 未找到我方伤害行动窗");
                return;
            }
            HardStop();
            BuildSession(report);
            StartCoroutine(PlayHighlightLoop(window));
        }

        /// <summary>重新打开战后结算表（播放完成后可用）。</summary>
        public void ShowSettlement()
        {
            if (_session?.Report == null) return;
            try
            {
                _session.Settlement ??= BattleSkillStatsAggregator.Build(_session.Report);
                SettlementPanel.Ensure().Show(_session.Settlement);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientBattle] 生成结算表失败: {e}");
            }
        }

        /// <summary>cut-in 请求（转发 CutInService.Request；去重在其内部）。</summary>
        public void RequestCutIn(string heroId, string text, int groupId)
        {
            if (_session?.Ctx != null)
                _session.Ctx.OnCutInRequested?.Invoke(heroId, text, groupId);
        }

        /// <summary>高伤补充门槛判定（SkillPerformance.SettleDamage 回调）；
        /// 阈值集中在 CutInPolicy。</summary>
        public void NotifyDamageSettled(DamageEvent damage, string floatName)
        {
            if (CutInPolicy.IsHighDamage(damage))
                RequestCutIn(damage.SourceId,
                    $"{floatName} 重创 {damage.TargetId}！-{damage.Amount}", damage.GroupId);
        }

        // ---------------------------------------------------------- 生命周期内部

        /// <summary>硬停止（R-1.2 唯一实现）：停协程 → 清全部在飞表现（含顶部横幅）→
        /// 杀残留 tween 兜底 → 停 BGM。任何状态下可调、幂等。</summary>
        void HardStop()
        {
            StopAllCoroutines();
            // 全局服务即使会话已拆也要停（Teardown / 重播竞态）
            if (CutInService.Instance != null) CutInService.Instance.CancelAll();
            CameraShaker.Cancel();
            UnitAuraService.ClearAll();
            BannerService.Instance?.Clear(); // 系列结束「胜者 X 队」等常驻横幅必须清（R-1.2③）
            var ctx = _session?.Ctx;
            if (ctx != null)
            {
                ctx.Vfx?.CancelAll();
                ctx.Floats?.CancelAll();
                ctx.Bubbles?.CancelAll();
                ctx.Sfx?.StopAll();
            }
            // 兜底核杀：单位位移/闪烁等 tween 已逐个 SetLink，但第三方/漏网 tween
            // 仍可能在跑（R-7.1 允许 HardStop 作为唯一 KillAll 出现点）
            DOTween.KillAll(complete: false);
            BgmLayerService.Instance?.StopBattle();
            State = _session != null ? PlaybackState.Finished : PlaybackState.Idle;
        }

        void BuildSession(BattleReport report)
        {
            State = PlaybackState.Building;
            Database = Database != null ? Database : PerformanceDatabase.LoadOrDefault();
            if (_board == null)
            {
                // 域重载（播放中热编译）会丢私有引用而留下孤儿棋盘：先收养再新建，
                // 避免场上出现双棋盘
                _board = FindFirstObjectByType<BattleBoardView>();
            }
            if (_board == null)
            {
                var boardGo = new GameObject("BattleBoard");
                _board = boardGo.AddComponent<BattleBoardView>();
            }
            _session = PlaybackWorldBuilder.Build(
                report, Database, _board, this, NotifyDamageSettled);
            _director = new PlaybackDirector(this);
            SettlementPanel.Ensure().Hide();
        }

        IEnumerator PlayLoop()
        {
            // 渲染级预热收尾（约 3 帧，属于加载期而非战斗期）后再开播：
            // 首个特效出场时 shader 编译/贴图上传已全部付清，不在战斗中掉帧
            State = PlaybackState.Prewarming;
            yield return new WaitUntil(() => _session.Ctx.Vfx.PrewarmComplete);
            State = PlaybackState.Playing;
            yield return _director.PlaySeries(_session);
            FinishPlayback(showSettlement: true);
        }

        IEnumerator PlayHighlightLoop(HighlightWindow window)
        {
            State = PlaybackState.Prewarming;
            yield return new WaitUntil(() => _session.Ctx.Vfx.PrewarmComplete);
            State = PlaybackState.Playing;
            _session.Ctx.OnBanner?.Invoke(
                $"★ 高光回放 — {window.ActorId}（单窗伤害 {window.Damage}）");
            var groups = _session.Pipeline.Run(
                _session.Report.Games[window.GameIndex].Events);
            yield return _director.PlayGroupsRange(
                _session, groups, window.StartSeq, window.EndSeq);
            _session.Ctx.OnBanner?.Invoke($"高光回放结束 — {window.ActorId}");
            FinishPlayback(showSettlement: true);
        }

        void FinishPlayback(bool showSettlement)
        {
            State = PlaybackState.Finished;
            if (showSettlement && _session?.Report != null)
            {
                _session.Settlement = BattleSkillStatsAggregator.Build(_session.Report);
                SettlementPanel.Ensure().Show(_session.Settlement);
            }
            OnAllComplete?.Invoke();
        }
    }
}
