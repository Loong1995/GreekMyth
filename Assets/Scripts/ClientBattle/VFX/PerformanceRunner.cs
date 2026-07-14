using System;
using System.Collections;
using System.Collections.Generic;
using ClientBattle.Audio;
using ClientBattle.Events;
using ClientBattle.Names;
using ClientBattle.Units;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】PerformanceRunner（MonoBehaviour 单例）：
    //
    // - 对外一键入口：PlayBattleReport(string json) + OnAllComplete 回调。
    // - 内部流程：解析战报 → 建棋盘 → 每局事件过 EventPipeline → 协程按序
    //   执行每组演出（EventGroup.ParallelWithNext 支持组间并行）。
    // - 节点组（回合横幅/单挑/终局）由 Runner 直接演出；
    //   战斗动作组交给 VFXResolver 解析出的 SkillPerformance。
    // - Speed 可调；SkipToEnd 时取消全部在飞演出、快进落账。
    // =========================================================================

    public class PerformanceRunner : MonoBehaviour
    {
        public static PerformanceRunner Instance { get; private set; }

        [Header("播放设置")]
        public float Speed = 1f;
        public PerformanceDatabase Database;

        [Header("节奏（呼吸间隙；常驻动画/待机呼吸/光环不受影响，继续播放）")]
        [Tooltip("每个英雄行动结束后的停顿秒数（应长于单元停顿）")]
        public float ActionPauseSeconds = 0.45f;
        [Tooltip("每个播放单元结束后的停顿秒数")]
        public float GroupPauseSeconds = 0.25f;

        public event Action OnAllComplete;
        public bool IsPlaying { get; private set; }

        BattleReport _report;
        BattleBoardView _board;
        VFXContext _ctx;
        VFXResolver _resolver;
        EventPipeline _pipeline;
        DefaultPerformance _defaultPerf;
        OracleAuraPerformance _oraclePerf;
        Coroutine _playLoop;
        string _banner = "";

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

        // ---------------------------------------------------------- 一键入口

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
            StopPlayback();
            _report = report;
            BuildWorld();
            _playLoop = StartCoroutine(PlayLoop());
        }

        /// <summary>跳到结尾：取消在飞演出，剩余事件静默落账，立刻回调完成。</summary>
        public void SkipToEnd()
        {
            if (!IsPlaying || _report == null) return;
            StopCoroutine(_playLoop);
            CancelAllFx();
            // 静默落账：直接把每局最后的兵力快照与阵亡状态刷上
            foreach (var game in _report.Games)
                foreach (var ev in game.Events)
                    ApplySilently(ev);
            FinishPlayback();
        }

        public void StopPlayback()
        {
            if (_playLoop != null) StopCoroutine(_playLoop);
            _playLoop = null;
            IsPlaying = false;
            CancelAllFx();
        }

        // ---------------------------------------------------------- 主循环

        void BuildWorld()
        {
            Database = Database != null ? Database : PerformanceDatabase.LoadOrDefault();
            _resolver = new VFXResolver(Database);
            _pipeline = new EventPipeline()
                .Register(new ReactionRegroupProcessor())        // 状态触发摘出，排主单元之后
                .Register(new CollectiveTriggerMergeProcessor()) // 雷霆等合并为一次集体齐发
                .Register(new NodeMergeProcessor());
            _defaultPerf = ScriptableObject.CreateInstance<DefaultPerformance>();
            _oraclePerf = ScriptableObject.CreateInstance<OracleAuraPerformance>();

            // 分辨率/宽高比自适配（不同机型统一由 CameraFitter 权威取景）
            CameraFitter.EnsureOn(Camera.main);

            if (_board == null)
            {
                var boardGo = new GameObject("BattleBoard");
                _board = boardGo.AddComponent<BattleBoardView>();
            }
            _board.Build(_report);

            var vfx = VFXManager.Ensure();
            vfx.Prewarm(); // 渲染级预热：shader/贴图/粒子网格全部压进加载期
            var floats = FloatingTextService.Ensure();

            _ctx = new VFXContext
            {
                Board = _board,
                Vfx = vfx,
                Floats = floats,
                Sfx = SfxManager.Ensure(),
                Bubbles = ChatBubbleService.Ensure(),
                SpeedScale = Speed,
            };
            PrewarmFromReport(floats); // 字形/音效/图标按本场战报内容前置生成
        }

        /// <summary>报告驱动预热：扫一遍战报事件，把战斗中会"第一次"产生分配或
        /// 纹理生成的东西（台词字形、名字字形、状态图标、合成音效、气泡对象）
        /// 全部在开战前做完。战斗热路径里从此只剩查缓存。</summary>
        void PrewarmFromReport(FloatingTextService floats)
        {
            var text = new System.Text.StringBuilder();
            var statusIds = new HashSet<string>();
            var sfxKeys = new HashSet<string>
            {
                "sfx_melee_default", "sfx_pursuit_default", "sfx_status_trigger_default",
                "sfx_active_default", "sfx_hit_default", "sfx_heal_default", "sfx_oracle_default",
                "sfx_defeated", "sfx_duel_horn", "sfx_duel_clash", "sfx_duel_win", "sfx_petrify_off",
            };

            foreach (var team in _report.Teams)
                foreach (var hero in team.Heroes)
                    text.Append(hero.HeroId);
            foreach (var game in _report.Games)
                foreach (var ev in game.Events)
                    switch (ev)
                    {
                        case TraitTriggerEvent trait:
                            text.Append(trait.Line); break;
                        case StatusApplyEvent apply when apply.Status != null:
                            statusIds.Add(apply.Status.StatusId); break;
                        case DuelChallengeEvent duel:
                            text.Append(duel.ChallengerId).Append(duel.DefenderId); break;
                    }
            foreach (var id in statusIds)
            {
                Units.StatusIconPanel.PrewarmIcon(id);
                sfxKeys.Add($"sfx_status_{id}");
            }
            // 特殊演出配置里的自定义音效 key 一并合成
            if (Database != null)
                foreach (var profile in Database.SpecialProfiles)
                {
                    if (!string.IsNullOrEmpty(profile.SfxKey)) sfxKeys.Add(profile.SfxKey);
                    if (!string.IsNullOrEmpty(profile.HitSfxKey)) sfxKeys.Add(profile.HitSfxKey);
                }
            foreach (var key in sfxKeys)
                Placeholder.PlaceholderFactory.GetAudio(key);

            floats.Prewarm(24, text.ToString()); // 飘字池 + 全部动态文本字形
            _ctx.Bubbles.Prewarm();              // 气泡对象与底图前置创建
        }

        IEnumerator PlayLoop()
        {
            IsPlaying = true;
            // 渲染级预热收尾（约 3 帧，属于加载期而非战斗期）后再开播：
            // 首个特效出场时 shader 编译/贴图上传已全部付清，不在战斗中掉帧
            yield return new WaitUntil(() => _ctx.Vfx.PrewarmComplete);
            for (int gameIdx = 0; gameIdx < _report.Games.Count; gameIdx++)
            {
                var game = _report.Games[gameIdx];
                if (gameIdx > 0)
                {
                    UnitAuraService.ClearAll(); // 整局光环随局重置
                    _board.ResetForNewGame();
                }
                SetBanner($"第 {game.GameNo} 局");

                var groups = _pipeline.Run(game.Events);
                bool actedSinceActionStart = false;
                foreach (var group in groups)
                {
                    _ctx.SpeedScale = Speed; // 支持播放中调速
                    if (group.ParallelWithNext)
                    {
                        ApplyGroupSilently(group); // 静默节点：即时落账不占节拍
                        continue;
                    }
                    // 上一个英雄行动收尾停顿（呼吸间隙；待机呼吸/光环/飘字照常播）
                    if (group.Root is ActionStartEvent && actedSinceActionStart)
                    {
                        actedSinceActionStart = false;
                        if (ActionPauseSeconds > 0f) yield return Wait(ActionPauseSeconds);
                    }
                    yield return PlayGroup(group);
                    if (IsActionKind(group.Kind))
                    {
                        actedSinceActionStart = true;
                        // 播放单元收尾停顿
                        if (GroupPauseSeconds > 0f) yield return Wait(GroupPauseSeconds);
                    }
                }

                SetBanner(game.WinnerTeamId != null
                    ? $"第 {game.GameNo} 局结束 — {game.WinnerTeamId} 队胜（{game.Reason}）"
                    : $"第 {game.GameNo} 局结束 — 平局（{game.Reason}）");
            }
            SetBanner(_report.SeriesWinnerTeamId != null
                ? $"系列结束 — 胜者 {_report.SeriesWinnerTeamId} 队"
                : "系列结束 — 平局");
            FinishPlayback();
        }

        IEnumerator PlayGroup(EventGroup group)
        {
            switch (group.Kind)
            {
                case GroupKind.Node:
                    yield return PlayNode(group);
                    break;
                case GroupKind.TraitLine:
                    // 性格台词：当场弹聊天框（不阻塞太久）
                    foreach (var ev in group.All<TraitTriggerEvent>())
                        _ctx.Bubbles.Say(_ctx.Unit(ev.HeroId), ev.Line);
                    break;
                case GroupKind.Duel:
                    yield return PlayDuel(group);
                    break;
                case GroupKind.Defeat:
                case GroupKind.StatusChange:
                    // 非行动组：即时落账（含阵亡倒下/状态图标/光环），不占时间轴
                    ApplyGroupSilently(group);
                    break;
                default:
                    var profile = _resolver.Resolve(group);
                    if (profile.Template == PerformanceTemplate.None)
                    {
                        foreach (var ev in group.Events) ApplySilently(ev);
                        yield break;
                    }
                    SkillPerformance performance =
                        profile.Template == PerformanceTemplate.OracleAura ? _oraclePerf : _defaultPerf;
                    yield return performance.Play(group, profile, _ctx);
                    break;
            }
        }

        IEnumerator PlayNode(EventGroup group)
        {
            switch (group.Root)
            {
                case RoundStartEvent round when round.RoundNo > 0:
                    SetBanner($"第 {round.RoundNo} 回合");
                    break;
                case ActionStartEvent action:
                    var unit = _ctx.Unit(action.ActorId);
                    if (unit != null && action.Skipped)
                    {
                        _ctx.Floats.Show(unit, "无法行动", new Color(0.7f, 0.7f, 0.8f), 1.0f);
                    }
                    break;
            }
            // 节点组的子事件必须落账：状态到期移除（石化解除等）/伤兵损耗/属性回写
            // 都挂在 round_start / action_start 之下，漏掉会造成图标与石化覆盖层残留
            foreach (var ev in group.Events)
                if (!ReferenceEquals(ev, group.Root))
                    ApplySilently(ev);
            yield break; // 节点只更新表现，不阻塞主播放队列
        }

        IEnumerator PlayDuel(EventGroup group)
        {
            var challenge = group.First<DuelChallengeEvent>();
            var result = group.First<DuelResultEvent>();
            if (challenge == null) yield break;

            // 非参战单位压暗，聚焦单挑双方
            foreach (var unit in _board.AllUnits)
                unit.SetDimmed(unit.Hero.HeroId != challenge.ChallengerId &&
                               unit.Hero.HeroId != challenge.DefenderId);
            SetBanner($"⚔ 单挑！{challenge.ChallengerId}（武{challenge.ChallengerForce}） vs " +
                      $"{challenge.DefenderId}（武{challenge.DefenderForce}）");
            _ctx.Sfx.Play("sfx_duel_horn");
            yield return Wait(1.0f);

            if (result != null && !result.Accepted)
            {
                SetBanner("对方拒绝了单挑");
                yield return Wait(0.8f);
            }
            else if (result != null)
            {
                // 三次对撞
                var a = _ctx.Unit(challenge.ChallengerId);
                var b = _ctx.Unit(challenge.DefenderId);
                for (int i = 0; i < 3 && a != null && b != null; i++)
                {
                    Vector3 mid = (a.HomePosition + b.HomePosition) / 2f;
                    // 各自朝己方出发方向留 0.4 间隙（与上下/左右布局无关）
                    Vector3 dirA = (a.HomePosition - mid).normalized;
                    Vector3 dirB = (b.HomePosition - mid).normalized;
                    var clashA = a.transform.DOMove(mid + dirA * 0.4f, Wait01(0.25f));
                    var clashB = b.transform.DOMove(mid + dirB * 0.4f, Wait01(0.25f));
                    yield return clashA.WaitForCompletion();
                    _ctx.Vfx.PlayAt("hit_clash", mid, Wait01(0.4f));
                    _ctx.Sfx.Play("sfx_duel_clash");
                    _ctx.Shake(0.18f, 0.2f);
                    yield return Wait(0.35f);
                    a.transform.DOMove(a.HomePosition, Wait01(0.2f));
                    b.transform.DOMove(b.HomePosition, Wait01(0.2f));
                    yield return Wait(0.3f);
                }
                SetBanner($"单挑胜者：{result.WinnerId}！");
                _ctx.Sfx.Play("sfx_duel_win");
                yield return Wait(0.8f);
                // 败者四维惩罚等副事件
                foreach (var ev in group.Events)
                    if (ev is AttrChangeEvent) SettleSide(ev);
                yield return Wait(0.5f);
            }
            foreach (var unit in _board.AllUnits) unit.SetDimmed(false);
            SetBanner("");
        }

        /// <summary>行动类播放单元（占用时间轴、结束后加节奏停顿）。</summary>
        static bool IsActionKind(GroupKind kind) =>
            kind is GroupKind.ActiveSkill or GroupKind.NormalAttack or GroupKind.Pursuit
                 or GroupKind.StatusTrigger or GroupKind.Duel;

        // ---------------------------------------------------------- 落账/工具

        void ApplyGroupSilently(EventGroup group)
        {
            foreach (var ev in group.Events) ApplySilently(ev);
        }

        /// <summary>不演出，只把结算权威值刷进视图（跳过/静默节点用）。</summary>
        void ApplySilently(BattleEvent ev)
        {
            switch (ev)
            {
                case DamageEvent d when d.Troops != null:
                    _ctx.Unit(d.TargetId)?.SetTroops(d.Troops.TroopsAfter); break;
                case HealEvent h when h.Troops != null:
                    _ctx.Unit(h.TargetId)?.SetTroops(h.Troops.TroopsAfter); break;
                case TroopsChangeEvent t when t.Troops != null:
                    _ctx.Unit(t.Troops.HeroId)?.SetTroops(t.Troops.TroopsAfter); break;
                case StatusApplyEvent apply when apply.Status != null:
                    var owner = _ctx.Unit(apply.Status.OwnerId);
                    owner?.StatusPanel.AddStatus(apply.Status.StatusId);
                    UnitAuraService.OnStatusApplied(owner, apply.Status.StatusId);
                    if (apply.Status.StatusId == "petrify") owner?.SetPetrified(true);
                    break;
                case StatusRemoveEvent remove when remove.Status != null:
                    var former = _ctx.Unit(remove.Status.OwnerId);
                    former?.StatusPanel.RemoveStatus(remove.Status.StatusId);
                    UnitAuraService.OnStatusRemoved(former, remove.Status.StatusId);
                    if (remove.Status.StatusId == "petrify") former?.SetPetrified(false);
                    break;
                case HeroDefeatedEvent defeated:
                    var fallen = _ctx.Unit(defeated.HeroId);
                    if (fallen != null && !fallen.Defeated)
                    {
                        fallen.PlayDefeated();
                        UnitAuraService.OnUnitDefeated(fallen);
                    }
                    break;
                case AttrChangeEvent attr:
                    SettleSide(attr); break;
            }
        }

        void SettleSide(BattleEvent ev)
        {
            if (ev is AttrChangeEvent attr)
            {
                var unit = _ctx.Unit(attr.HeroId);
                foreach (var change in attr.Changes)
                    _ctx.Floats.ShowAttr(unit, ChineseNames.Attr(change.Attr),
                        change.After - change.Before);
            }
        }

        void CancelAllFx()
        {
            if (_ctx == null) return;
            UnitAuraService.ClearAll();
            _ctx.Vfx.CancelAll();
            _ctx.Floats.CancelAll();
            _ctx.Bubbles.CancelAll();
            _ctx.Sfx.StopAll();
            CameraShaker.Cancel();
        }

        void FinishPlayback()
        {
            IsPlaying = false;
            _playLoop = null;
            OnAllComplete?.Invoke();
        }

        WaitForSeconds Wait(float seconds) =>
            new(seconds / Mathf.Max(0.1f, Speed));

        float Wait01(float seconds) => seconds / Mathf.Max(0.1f, Speed);

        void SetBanner(string text) => _banner = text;

        GUIStyle _bannerStyle; // 缓存：OnGUI 每帧 new GUIStyle 会产生 GC 压力

        void OnGUI()
        {
            if (string.IsNullOrEmpty(_banner)) return;
            // 按屏幕高度缩放字号（以 800px 高为基准），高分屏手机上不至于过小
            float k = Mathf.Max(1f, Screen.height / 800f);
            _bannerStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            var style = _bannerStyle;
            style.fontSize = Mathf.RoundToInt(26 * k);
            // 阴影+白字双绘：任何底色（无色黑/白图背景）都可读
            var rect = new Rect(0, 24 * k, Screen.width, 40 * k);
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), _banner, style);
            style.normal.textColor = Color.white;
            GUI.Label(rect, _banner, style);
        }
    }
}
