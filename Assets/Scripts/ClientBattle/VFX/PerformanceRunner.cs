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
    //   cut-in 策略  → Events/CutInPlanner（编译期注记）
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

        // ---------------------------------------------------------- 播放时钟（手动校验用）

        float _clockStart;      // 本次播放真正开播（预热之后）的时刻
        float _clockOffset;     // 跳播起点在整场时间轴上的秒数
        float _clockFrozen;     // 播完/停止后定格的读数

        /// <summary>整场时间轴上的当前位置（秒）：跳播起点偏移 + 开播至今的真实秒。
        /// 预热不计入。供 <see cref="PlaybackTimelineBar"/> 与人工对时用。</summary>
        public float TimelineSeconds =>
            State == PlaybackState.Playing
                ? _clockOffset + (Time.realtimeSinceStartup - _clockStart)
                : _clockFrozen;

        /// <summary>编译产物（只读）：时间轴条据此算回合刻度。</summary>
        public CompiledPlayback Compiled => _session?.Compiled;
        public BattleReport Report => _session?.Report;

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
            _clockOffset = 0f;
            StartCoroutine(PlayLoop());
        }

        /// <summary>跳播：从某局某 seq 起正常演出，之前的一律静默落账（终态等价）。
        /// <paramref name="timelineOffset"/> 只影响播放时钟读数（对时用）。
        /// 时间轴条点回合刻度即走本入口。</summary>
        public void PlayFrom(int gameIndex, int startSeq, float timelineOffset = 0f)
        {
            if (_session?.Report == null) return;
            var report = _session.Report;
            HardStop();
            BuildSession(report);
            _clockOffset = Mathf.Max(0f, timelineOffset);
            StartCoroutine(PlayLoop(gameIndex, startSeq));
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

        /// <summary>伤害落定回调（<c>SkillPerformance.SettleDamage</c>）。
        ///
        /// **巨伤 cut-in 不在这里请求**：它由 <c>CutInPlanner</c> 在编译期、播组
        /// 之前预判，走 <c>CutInStage</c> 的「推镜→横幅→出手命中→撤镜」独占单元
        /// （2026-07-27 统一）。事后请求既做不到伤害前推镜，暗幕还会盖住刚起播的
        /// 命中特效（P-72）。本回调保留给高光选窗等纯观测方，勿再挂表现。</summary>
        public void NotifyDamageSettled(DamageEvent damage, string floatName) { }

        // ---------------------------------------------------------- 生命周期内部

        /// <summary>硬停止（R-1.2 唯一实现）：停协程 → 清全部在飞表现（含顶部横幅）→
        /// 杀残留 tween 兜底 → 停 BGM。任何状态下可调、幂等。</summary>
        void HardStop()
        {
            StopAllCoroutines();
            _clockFrozen = TimelineSeconds;
            ClearAllPresentation();
            // 兜底核杀：单位位移/闪烁等 tween 已逐个 SetLink，但第三方/漏网 tween
            // 仍可能在跑（R-7.1 允许 HardStop 作为唯一 KillAll 出现点）
            DOTween.KillAll(complete: false);
            if (BgmLayerService.Instance != null) BgmLayerService.Instance.StopBattle();
            State = _session != null ? PlaybackState.Finished : PlaybackState.Idle;
        }

        /// <summary>清空**一切在飞表现**（R-1.2 ③ 的唯一实现处）。
        ///
        /// 逐个走**全局单例**而不是只走 `_session.Ctx`：重播/跳播会重建会话，
        /// 上一份 ctx 里的引用可能已丢（域重载、Teardown 竞态），只清 ctx 就会
        /// 留下「上一场的台词气泡/飘字还挂在场上」——2026-07-28 人工实测到的残留。
        /// ctx 侧照旧再清一遍（幂等，覆盖自定义注入的实例）。
        ///
        /// **禁止**对 Unity 对象用 <c>?.</c>：已 Destroy 的实例 C# 引用非 null，
        /// <c>?.</c> 会放行，随即 MissingReferenceException（VFXManager 重播首帧
        /// 实锤）。一律 <c>if (x != null)</c>（走 Unity 重载）。</summary>
        void ClearAllPresentation()
        {
            if (CutInService.Instance != null) CutInService.Instance.CancelAll();
            CameraShaker.Cancel();
            StageCameraRig.ReleaseAll(); // 硬停止在推镜途中：不还位就一直卡在近机位
            AfterImageService.ClearAll();
            UnitAuraService.ClearAll();
            if (BannerService.Instance != null) BannerService.Instance.Clear();
            if (ChatBubbleService.Instance != null) ChatBubbleService.Instance.CancelAll();
            if (FloatingTextService.Instance != null) FloatingTextService.Instance.CancelAll();
            if (VFXManager.Instance != null) VFXManager.Instance.CancelAll();
            if (Audio.SfxManager.Instance != null) Audio.SfxManager.Instance.StopAll();
            var ctx = _session?.Ctx;
            if (ctx != null)
            {
                if (ctx.Vfx != null) ctx.Vfx.CancelAll();
                if (ctx.Floats != null) ctx.Floats.CancelAll();
                if (ctx.Bubbles != null) ctx.Bubbles.CancelAll();
                if (ctx.Sfx != null) ctx.Sfx.StopAll();
            }
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

        IEnumerator PlayLoop(int fromGameIndex = 0, int fromSeq = 0)
        {
            // 渲染级预热收尾（约 3 帧，属于加载期而非战斗期）后再开播：
            // 首个特效出场时 shader 编译/贴图上传已全部付清，不在战斗中掉帧
            State = PlaybackState.Prewarming;
            yield return new WaitUntil(() => _session.Ctx.Vfx.PrewarmComplete);
            State = PlaybackState.Playing;
            _clockStart = Time.realtimeSinceStartup; // 预热不计入播放时钟
            yield return _director.PlaySeries(_session, fromGameIndex, fromSeq);
            FinishPlayback(showSettlement: true);
        }

        IEnumerator PlayHighlightLoop(HighlightWindow window)
        {
            State = PlaybackState.Prewarming;
            yield return new WaitUntil(() => _session.Ctx.Vfx.PrewarmComplete);
            State = PlaybackState.Playing;
            _session.Ctx.OnBanner?.Invoke(
                $"★ 高光回放 — {window.ActorId}（单窗伤害 {window.Damage}）");
            var groups = _session.Compiled.GroupsOf(window.GameIndex);
            yield return _director.PlayGroupsRange(
                _session, groups, window.StartSeq, window.EndSeq);
            _session.Ctx.OnBanner?.Invoke($"高光回放结束 — {window.ActorId}");
            FinishPlayback(showSettlement: true);
        }

        void FinishPlayback(bool showSettlement)
        {
            _clockFrozen = TimelineSeconds; // 定格读数（先算后改状态）
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
