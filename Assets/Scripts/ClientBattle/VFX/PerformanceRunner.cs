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
        [Tooltip("全局时长倍率：动画节拍与单元/行动停顿一并放大（默认 2=放慢一倍看清战报）")]
        public float DurationMul = 2f;
        public PerformanceDatabase Database;

        [Header("节奏（呼吸间隙；常驻动画/待机呼吸/光环不受影响，继续播放）")]
        [Tooltip("每个英雄行动结束后的停顿秒数（应长于单元停顿；再乘 DurationMul）")]
        public float ActionPauseSeconds = 0.55f;
        [Tooltip("每个播放单元结束后的停顿秒数（再乘 DurationMul）")]
        public float GroupPauseSeconds = 0.35f;
        [Tooltip("已废弃：台词独占时长改由 ChatBubbleService.ExclusiveSeconds 驱动")]
        public float TraitLinePauseSeconds = 0.5f;

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

        // ---- cut-in 通道（B2/C10）：非阻塞横幅，满档轨触发/高伤/追伤第 5 次 ----
        string _cutInText = "";
        float _cutInUntil;          // Time.time 到期即淡出
        int _cutInGroupId = -1;     // 同一次结算（同组）只播 1 次（去重）
        int _pursuitCountInWindow;  // 当前行动窗内追击单元计数（补充门槛：第 5 次）
        const float HighDamageCutIn = 3000f;

        // ---- 战后结算表（三谋式：分队/分武将/分技能 杀伤+治疗；多局可切 Tab）----
        bool _showSettlement;
        BattleSettlementSnapshot _settlement;
        Vector2 _settlementScroll;
        int _settlementTab; // Games 列表下标

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
            BgmLayerService.Instance?.StopBattle();
        }

        // ---------------------------------------------------------- 主循环

        void BuildWorld()
        {
            Database = Database != null ? Database : PerformanceDatabase.LoadOrDefault();
            _resolver = new VFXResolver(Database);
            _pipeline = new EventPipeline()
                .Register(new ReactionRegroupProcessor())        // 状态触发摘出，排主单元之后
                .Register(new CollectiveTriggerMergeProcessor()) // 雷霆等合并为一次集体齐发
                .Register(new TraitLineExtractProcessor())      // 台词拆成独占 TraitLine 组
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
                DurationMul = DurationMul,
                TraitLinePauseSeconds = TraitLinePauseSeconds,
            };
            _showSettlement = false;
            _settlement = null;
            _settlementTab = 0;
            PrewarmFromReport(floats); // 字形/音效/图标按本场战报内容前置生成
            BgmLayerService.Ensure().StartBattle(); // B3：stem/占位单曲同相位起播
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
                    MomentumService.ClearAll(); // 势能账本随局重置
                    _board.ResetForNewGame();
                }
                SetBanner($"第 {game.GameNo} 局");

                var groups = _pipeline.Run(game.Events);
                bool actedSinceActionStart = false;
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var group = groups[gi];
                    _ctx.SpeedScale = Speed;
                    _ctx.DurationMul = DurationMul;
                    if (group.ParallelWithNext)
                    {
                        ApplyGroupSilently(group);
                        continue;
                    }
                    if (group.Root is ActionStartEvent && actedSinceActionStart)
                    {
                        actedSinceActionStart = false;
                        if (ActionPauseSeconds > 0f) yield return Wait(ActionPauseSeconds);
                    }
                    yield return PlayGroup(group);
                    if (IsActionKind(group.Kind))
                    {
                        actedSinceActionStart = true;
                        // 下一组是台词：不加单元停顿（台词独占单元与邻组无缝衔接）
                        bool nextTrait = gi + 1 < groups.Count
                                         && groups[gi + 1].Kind == GroupKind.TraitLine
                                         && !groups[gi + 1].ParallelWithNext;
                        if (GroupPauseSeconds > 0f && !nextTrait)
                            yield return Wait(GroupPauseSeconds);
                    }
                    // TraitLine 本身不加 GroupPause，播完立刻接下一段
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
                    // 独占播放单元：气泡时长内阻塞时间轴；播完无缝接下组（无额外停顿）
                    foreach (var ev in group.All<TraitTriggerEvent>())
                    {
                        float hold = _ctx.Bubbles.SayExclusive(_ctx.Unit(ev.HeroId), ev.Line);
                        if (hold > 0f) yield return Wait(hold);
                    }
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
                    // 犹豫延迟宣告：台词已在前一 TraitLine 组弹出；此处补飘字「延迟」
                    if (group.Root is SkillTriggerEvent { Kind: "delayed" } delayedSt)
                    {
                        var delayedUnit = _ctx.Unit(delayedSt.ActorId);
                        if (delayedUnit != null)
                            _ctx.Floats.Show(delayedUnit, "延迟", new Color(0.75f, 0.7f, 1f), 1.1f);
                        yield return Wait(0.35f);
                        foreach (var ev in group.Events) ApplySilently(ev);
                        yield break;
                    }
                    // 追伤第 5 次补充门槛（C10）：行动窗内第 5 个追击单元 cut-in
                    if (group.Kind == GroupKind.Pursuit && ++_pursuitCountInWindow == 5)
                    {
                        var pursuer = group.Root is SkillTriggerEvent pst ? pst.ActorId : null;
                        RequestCutIn($"{pursuer} 追击不止！", group.Root.GroupId);
                    }
                    // 连发演出（B1）：第 2 次起节拍加速 + 计数角标
                    bool burst = group.Root is SkillTriggerEvent { BurstNo: >= 2 };
                    if (burst)
                    {
                        var st = (SkillTriggerEvent)group.Root;
                        _ctx.TempoScale = 1.35f;
                        var caster = _ctx.Unit(st.ActorId);
                        if (caster != null)
                            _ctx.Floats.Show(caster, $"连发 ×{st.BurstNo}",
                                new Color(1f, 0.85f, 0.3f), 1.15f);
                    }
                    SkillPerformance performance =
                        profile.Template == PerformanceTemplate.OracleAura ? _oraclePerf : _defaultPerf;
                    yield return performance.Play(group, profile, _ctx);
                    if (burst) _ctx.TempoScale = 1f;
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
                    // 自身行动窗开始：四轨势能镜像清零（与服务器静默清零同步）
                    MomentumService.OnActionStart(action.ActorId, unit);
                    _pursuitCountInWindow = 0;
                    if (unit != null && action.Skipped)
                    {
                        _ctx.Floats.Show(unit, "无法行动", new Color(0.7f, 0.7f, 0.8f), 1.0f);
                    }
                    break;
                case MomentumChangeEvent momentum: // 独立组根的势能事件（少见）：正常落账
                    MomentumService.Apply(momentum, _ctx.Unit(momentum.HeroId));
                    if (momentum.CutIn &&
                        MomentumService.TrackTable.TryGetValue(momentum.Track, out var style))
                        RequestCutIn($"{momentum.HeroId} 势能全开·{style.Label}！", momentum.GroupId);
                    break;
            }
            // 节点组子事件落账；台词已由 TraitLineExtract 抽走，此处不再弹气泡
            foreach (var ev in group.Events)
            {
                if (ReferenceEquals(ev, group.Root)) continue;
                if (ev is TraitTriggerEvent) continue; // 防御：漏网台词不静默吞、也不在此播
                ApplySilently(ev);
            }
            yield break;
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
            BgmLayerService.Instance?.Duck(); // 单挑全层 duck（B3）
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

        // ---------------------------------------------------------- 高光回放（B6/C2）

        /// <summary>终局高光回放：遍历指定队伍每武将的全部行动窗
        /// （action_start 分界），按**单窗伤害量**取最大窗整段重播。
        /// 纯客户端二次剪辑：窗前事件静默落账、窗内事件正常演出。</summary>
        public void PlayHighlight(string teamId = "A")
        {
            if (_report == null) { Debug.LogWarning("[ClientBattle] 无战报，先播放一场"); return; }
            var ourHeroes = new HashSet<string>();
            foreach (var team in _report.Teams)
                if (team.TeamId == teamId)
                    foreach (var hero in team.Heroes) ourHeroes.Add(hero.HeroId);

            int bestGame = -1, bestStart = 0, bestEnd = 0, bestTotal = 0;
            string bestActor = null;
            for (int gi = 0; gi < _report.Games.Count; gi++)
            {
                var events = _report.Games[gi].Events;
                string actor = null; int start = 0, total = 0;
                void CloseWindow(int endSeq)
                {
                    if (actor != null && total > bestTotal)
                    { bestGame = gi; bestStart = start; bestEnd = endSeq;
                      bestTotal = total; bestActor = actor; }
                }
                foreach (var ev in events)
                {
                    if (ev is ActionStartEvent action)
                    {
                        CloseWindow(ev.Seq);
                        bool ours = ourHeroes.Contains(action.ActorId);
                        actor = ours ? action.ActorId : null;
                        start = ev.Seq;
                        total = 0;
                    }
                    else if (actor != null && ev is DamageEvent d
                             && string.IsNullOrEmpty(d.Mitigation) && ourHeroes.Contains(d.SourceId))
                        total += d.Amount;
                }
                CloseWindow(int.MaxValue);
            }
            if (bestActor == null) { Debug.LogWarning("[ClientBattle] 未找到我方伤害行动窗"); return; }
            StopPlayback();
            _playLoop = StartCoroutine(PlayHighlightLoop(bestGame, bestStart, bestEnd, bestActor, bestTotal));
        }

        IEnumerator PlayHighlightLoop(int gameIdx, int startSeq, int endSeq, string actorId, int total)
        {
            IsPlaying = true;
            BuildWorld();
            yield return new WaitUntil(() => _ctx.Vfx.PrewarmComplete);
            SetBanner($"★ 高光回放 — {actorId}（单窗伤害 {total}）");
            var groups = _pipeline.Run(_report.Games[gameIdx].Events);
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                int rootSeq = group.Root.Seq;
                if (rootSeq >= endSeq) break;
                if (rootSeq < startSeq) { ApplyGroupSilently(group); continue; }
                _ctx.SpeedScale = Speed;
                _ctx.DurationMul = DurationMul;
                if (group.ParallelWithNext) { ApplyGroupSilently(group); continue; }
                yield return PlayGroup(group);
                if (IsActionKind(group.Kind))
                {
                    bool nextTrait = gi + 1 < groups.Count
                                     && groups[gi + 1].Kind == GroupKind.TraitLine
                                     && !groups[gi + 1].ParallelWithNext;
                    if (GroupPauseSeconds > 0f && !nextTrait)
                        yield return Wait(GroupPauseSeconds);
                }
            }
            SetBanner($"高光回放结束 — {actorId}");
            FinishPlayback();
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
                case MomentumChangeEvent momentum:
                    MomentumService.Apply(momentum, _ctx.Unit(momentum.HeroId), silent: true);
                    break;
                case TacticAppliedEvent tactic: // 战术变更：非阻塞横幅播报（1.4.1）
                    RequestCutIn(
                        $"{tactic.TeamId} 队变更战术 →「{ChineseNames.Status(tactic.TacticId)}」",
                        tactic.GroupId);
                    break;
                case ActionStartEvent action:
                    MomentumService.OnActionStart(action.ActorId, _ctx.Unit(action.ActorId));
                    break;
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
            if (_report != null)
            {
                _settlement = BattleSkillStatsAggregator.Build(_report);
                _settlementTab = 0;
                _showSettlement = true;
            }
            OnAllComplete?.Invoke();
        }

        /// <summary>重新打开战后结算表（播放完成后可用）。</summary>
        public void ShowSettlement()
        {
            if (_report == null) return;
            try
            {
                _settlement ??= BattleSkillStatsAggregator.Build(_report);
                if (_settlement.Games.Count == 0)
                {
                    Debug.LogWarning("[ClientBattle] 结算表为空（战报无对局）");
                    return;
                }
                _settlementTab = Mathf.Clamp(_settlementTab, 0, _settlement.Games.Count - 1);
                _showSettlement = true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ClientBattle] 生成结算表失败: {e}");
            }
        }

        WaitForSeconds Wait(float seconds) =>
            new(seconds * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, Speed));

        float Wait01(float seconds) =>
            seconds * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, Speed);

        void SetBanner(string text) => _banner = text;

        /// <summary>cut-in 请求（非阻塞横幅通道，复用单挑横幅样式的强化版）。
        /// 同一播放组（=同一次结算）只播 1 次；高频满档 cut-in 为设计意图，
        /// 不做回合级限流（C10）。触发源：满档轨 momentum cut_in / 高伤 /
        /// 追伤第 5 次。不占时间轴，只改画面强度。</summary>
        public void RequestCutIn(string text, int groupId)
        {
            if (groupId == _cutInGroupId) return;
            _cutInGroupId = groupId;
            _cutInText = text;
            _cutInUntil = Time.time + 1.4f * Mathf.Max(0.1f, DurationMul);
            CameraShaker.Shake(0.12f, 0.18f);
            BgmLayerService.Instance?.Duck(); // cut-in 全层 duck（B3）
        }

        /// <summary>高伤补充门槛判定（SkillPerformance.SettleDamage 回调）。</summary>
        public void NotifyDamageSettled(DamageEvent damage, string floatName)
        {
            if (damage.Amount > HighDamageCutIn)
                RequestCutIn($"{floatName} 重创 {damage.TargetId}！", damage.GroupId);
        }

        GUIStyle _bannerStyle; // 缓存：OnGUI 每帧 new GUIStyle 会产生 GC 压力
        GUIStyle _settleTitleStyle, _settleHeroStyle, _settleSkillStyle, _settleBtnStyle;

        void OnGUI()
        {
            // 按屏幕高度缩放字号（以 800px 高为基准），高分屏手机上不至于过小
            float k = Mathf.Max(1f, Screen.height / 800f);
            if (_showSettlement && _settlement != null)
            {
                DrawSettlement(k);
                return;
            }
            DrawCutIn(k);
            if (string.IsNullOrEmpty(_banner)) return;
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

        /// <summary>三谋式战后结算：左右分队；多局时顶部 Tab 切换分局/系列合计。</summary>
        void DrawSettlement(float k)
        {
            if (_settlement == null || _settlement.Games.Count == 0) return;
            _settlementTab = Mathf.Clamp(_settlementTab, 0, _settlement.Games.Count - 1);
            var snap = _settlement.Games[_settlementTab];

            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

            _settleTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            };
            _settleBtnStyle ??= new GUIStyle(GUI.skin.button);
            _settleBtnStyle.fontSize = Mathf.RoundToInt(14 * k);

            // 系列胜负标题
            _settleTitleStyle.fontSize = Mathf.RoundToInt(28 * k);
            _settleTitleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            string series = string.IsNullOrEmpty(_settlement.SeriesWinnerTeamId) ? "系列平局"
                : $"系列胜者 {_settlement.SeriesWinnerTeamId} 队";
            GUI.Label(new Rect(0, 6 * k, Screen.width, 32 * k), series, _settleTitleStyle);

            // 分局 Tab
            float tabY = 40 * k;
            float tabH = 28 * k;
            float tabW = 100 * k;
            float tabsWidth = _settlement.Games.Count * (tabW + 6 * k);
            float tabX0 = (Screen.width - tabsWidth) * 0.5f;
            for (int i = 0; i < _settlement.Games.Count; i++)
            {
                var g = _settlement.Games[i];
                var label = g.GameNo == 0 ? "系列合计" : $"第 {g.GameNo} 局";
                if (GUI.Toggle(new Rect(tabX0 + i * (tabW + 6 * k), tabY, tabW, tabH),
                        _settlementTab == i, label, _settleBtnStyle) && _settlementTab != i)
                {
                    _settlementTab = i;
                    _settlementScroll = Vector2.zero;
                }
            }

            // 本 Tab 胜负副标题
            _settleTitleStyle.fontSize = Mathf.RoundToInt(20 * k);
            _settleTitleStyle.normal.textColor = Color.white;
            string winner = string.IsNullOrEmpty(snap.WinnerTeamId) ? $"{snap.Title} · 平局"
                : $"{snap.Title} · {snap.WinnerTeamId} 队胜";
            GUI.Label(new Rect(0, 72 * k, Screen.width, 28 * k), winner, _settleTitleStyle);

            float mid = Screen.width * 0.5f;
            float colW = Screen.width * 0.42f;
            float leftX = mid - colW - 8 * k;
            float rightX = mid + 8 * k;
            float top = 104 * k;
            float height = Screen.height - top - 56 * k;

            _settlementScroll = GUI.BeginScrollView(
                new Rect(0, top, Screen.width, height),
                _settlementScroll,
                new Rect(0, 0, Screen.width - 20 * k, Mathf.Max(height, EstimateSettlementHeight(snap, k))));

            DrawTeamColumn(snap.TeamA, leftX, 0, colW, k, isWinner: snap.WinnerTeamId == snap.TeamAId);
            DrawTeamColumn(snap.TeamB, rightX, 0, colW, k, isWinner: snap.WinnerTeamId == snap.TeamBId);

            GUI.EndScrollView();

            _settleBtnStyle.fontSize = Mathf.RoundToInt(16 * k);
            float bw = 140 * k, bh = 36 * k;
            if (GUI.Button(new Rect(mid - bw * 0.5f, Screen.height - 48 * k, bw, bh),
                    "关闭结算", _settleBtnStyle))
                _showSettlement = false;
        }

        float EstimateSettlementHeight(GameSettlementSnapshot snap, float k)
        {
            int rows = 0;
            foreach (var h in snap.TeamA) rows += 2 + h.Skills.Count;
            foreach (var h in snap.TeamB) rows += 2 + h.Skills.Count;
            return rows * 22 * k + 80 * k;
        }

        void DrawTeamColumn(List<HeroSkillStats> heroes, float x, float y, float w, float k, bool isWinner)
        {
            _settleHeroStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _settleSkillStyle ??= new GUIStyle(GUI.skin.label);
            _settleHeroStyle.fontSize = Mathf.RoundToInt(16 * k);
            _settleSkillStyle.fontSize = Mathf.RoundToInt(13 * k);

            float cy = y;
            foreach (var hero in heroes)
            {
                float ratio = hero.MaxTroops > 0
                    ? Mathf.Clamp01((float)hero.FinalTroops / hero.MaxTroops) : 0f;
                // 兵力条
                var barBg = new Rect(x, cy, w, 18 * k);
                GUI.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
                GUI.DrawTexture(barBg, Texture2D.whiteTexture);
                GUI.color = isWinner ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.45f, 0.45f, 0.5f);
                GUI.DrawTexture(new Rect(x, cy, w * ratio, 18 * k), Texture2D.whiteTexture);
                GUI.color = Color.white;
                _settleHeroStyle.normal.textColor = Color.white;
                GUI.Label(barBg, $" {hero.HeroId}  {hero.FinalTroops}/{hero.MaxTroops}",
                    _settleHeroStyle);
                cy += 22 * k;

                foreach (var skill in hero.Skills)
                {
                    if (skill.Triggers <= 0 && skill.Damage <= 0 && skill.Heal <= 0) continue;
                    string name = skill.DisplayName;
                    string line = $"  {name}  ×{skill.Triggers}";
                    if (skill.Damage > 0) line += $"  ⚔{skill.Damage}";
                    if (skill.Heal > 0) line += $"  +{skill.Heal}";
                    _settleSkillStyle.normal.textColor = new Color(0.9f, 0.9f, 0.85f);
                    GUI.Label(new Rect(x, cy, w, 18 * k), line, _settleSkillStyle);
                    cy += 18 * k;
                }
                cy += 10 * k;
            }
        }

        GUIStyle _cutInStyle;

        void DrawCutIn(float k)
        {
            if (Time.time >= _cutInUntil || string.IsNullOrEmpty(_cutInText)) return;
            float alpha = Mathf.Clamp01((_cutInUntil - Time.time) / 0.4f); // 末段淡出
            _cutInStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            };
            _cutInStyle.fontSize = Mathf.RoundToInt(34 * k);
            var rect = new Rect(0, Screen.height * 0.30f, Screen.width, 50 * k);
            _cutInStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), _cutInText, _cutInStyle);
            _cutInStyle.normal.textColor = new Color(1f, 0.9f, 0.35f, alpha); // 金字
            GUI.Label(rect, _cutInText, _cutInStyle);
        }
    }
}
