using System;
using System.Collections;
using System.Collections.Generic;
using ClientBattle.Audio;
using ClientBattle.Events;
using ClientBattle.Test;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】PerformanceRunner（MonoBehaviour 单例）：纯编排层。
    //
    // - 对外一键入口：PlayBattleReport(string json) + OnAllComplete 回调。
    // - 内部流程：解析战报 → 建棋盘 → 每局事件过 EventPipeline → 协程按序
    //   执行每组演出（EventGroup.ParallelWithNext 支持组间并行）。
    // - 职责边界（2026-07-22 结构性重构）：
    //     落账        → EventApplyService（Silent/Animated 唯一入口）
    //     横幅/文字回退 → BannerService
    //     cut-in 去重与分发 → CutInService.Request
    //     单挑        → DuelPerformance；高光选窗 → HighlightSelector
    //     势能火生命周期 → MomentumFireController（Runner 只发相位信号）
    //     战后结算表   → SettlementPanel
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
        public event Action OnAllComplete;
        public bool IsPlaying { get; private set; }

        const float HighDamageCutIn = 3000f;

        BattleReport _report;
        BattleBoardView _board;
        VFXContext _ctx;
        VFXResolver _resolver;
        EventPipeline _pipeline;
        DefaultPerformance _defaultPerf;
        OracleAuraPerformance _oraclePerf;
        DuelPerformance _duelPerf;
        Coroutine _playLoop;
        int _pursuitCountInWindow; // 当前行动窗内追击单元计数（cut-in 补充门槛：第 5 次）
        BattleSettlementSnapshot _settlement;

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
                    EventApplyService.Apply(ev, _ctx, animated: false);
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

        // ---------------------------------------------------------- 世界构建

        void BuildWorld()
        {
            Database = Database != null ? Database : PerformanceDatabase.LoadOrDefault();
            _resolver = new VFXResolver(Database);
            _pipeline = new EventPipeline()
                // 借刀（代战/披甲）按段拆单元并回插事件流原生位置：
                // 段1(借手)→响应→追伤→段2…（不拆会三刀连劈再补账）
                .Register(new BorrowBladeSplitProcessor(
                    g => _resolver.Resolve(g).BorrowBlade))
                .Register(new ReactionRegroupProcessor())        // 状态触发摘出，排主单元之后
                .Register(new CollectiveTriggerMergeProcessor()) // 雷霆等合并为一次集体齐发
                .Register(new TraitLineExtractProcessor())      // 台词拆成独占 TraitLine 组
                .Register(new NodeMergeProcessor());
            _defaultPerf = ScriptableObject.CreateInstance<DefaultPerformance>();
            _oraclePerf = ScriptableObject.CreateInstance<OracleAuraPerformance>();
            _duelPerf = ScriptableObject.CreateInstance<DuelPerformance>();

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
            var banner = BannerService.Ensure();
            var cutIn = CutInService.Ensure();

            _ctx = new VFXContext
            {
                Board = _board,
                Vfx = vfx,
                Floats = floats,
                Sfx = SfxManager.Ensure(),
                Bubbles = ChatBubbleService.Ensure(),
                SpeedScale = Speed,
                DurationMul = DurationMul,
                // 编排层回调注入：演出执行层（SkillPerformance 族）零 Runner 依赖
                OnDamageSettled = NotifyDamageSettled,
                OnCutInRequested = (heroId, text, groupId) =>
                    cutIn.Request(_ctx, heroId, text, groupId),
                OnBanner = banner.Set,
            };
            // 领域账本（MomentumService）与 Audio 层解耦：编排层接线
            MomentumService.GlobalMomentumChanged =
                total => BgmLayerService.Instance?.SetGlobalMomentum(total);
            // 重播复位：势能镜像/光环随世界重建清零（主循环只在 gameIdx>0 清，
            // 重播第 1 局会带上一次播放的残账）；cut-in 组去重同战报重播必撞
            // 相同 group_id，不复位会吞掉高伤/满档切入
            MomentumService.ClearAll();
            UnitAuraService.ClearAll();
            cutIn.ResetDedup();
            SettlementPanel.Ensure().Hide();
            _settlement = null;
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
                "sfx_cutin_solo", "sfx_attack_empowered",
            };
            text.Append("VS势能全开追击不止重创单挑"); // cut-in 固定文案字形预热

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
                StatusIconPanel.PrewarmIcon(id);
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

        // ---------------------------------------------------------- 主循环

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
                yield return PlayGroupsRange(groups, 0, int.MaxValue);

                SetBanner(game.WinnerTeamId != null
                    ? $"第 {game.GameNo} 局结束 — {game.WinnerTeamId} 队胜（{game.Reason}）"
                    : $"第 {game.GameNo} 局结束 — 平局（{game.Reason}）");
            }
            SetBanner(_report.SeriesWinnerTeamId != null
                ? $"系列结束 — 胜者 {_report.SeriesWinnerTeamId} 队"
                : "系列结束 — 平局");
            FinishPlayback();
        }

        /// <summary>按序播放 [startSeq, endSeq) 内的组；范围外前缀静默落账。
        /// 主循环与高光回放共用（高光 = 带窗口范围的二次剪辑）。</summary>
        IEnumerator PlayGroupsRange(List<EventGroup> groups, int startSeq, int endSeq)
        {
            bool actedSinceActionStart = false;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                if (group.Root.Seq >= endSeq) break;
                if (group.Root.Seq < startSeq)
                {
                    ApplyGroupSilently(group);
                    continue;
                }
                _ctx.SpeedScale = Speed;
                _ctx.DurationMul = DurationMul;
                if (group.ParallelWithNext)
                {
                    ApplyGroupSilently(group);
                    continue;
                }
                // 势能火相位信号：回合横幅前提前开渐灭（末位行动→下回合之间
                // 往往没有立刻 ActionStart）
                if (group.Root is RoundStartEvent && actedSinceActionStart)
                    MomentumFireController.OnRoundBanner(
                        _board, Wait01(Mathf.Max(0.2f, ActionPauseSeconds)));
                // 上一行动窗已打出过行动类单元 → 进入下一 action_start 前 ActionPause：
                // 场上势能火在此停顿内渐灭（不依赖 lastActionActor；避免借刀/响应归账漏灭）
                if (group.Root is ActionStartEvent && actedSinceActionStart)
                {
                    actedSinceActionStart = false;
                    if (ActionPauseSeconds > 0f)
                    {
                        MomentumFireController.OnActionPauseBegin(
                            _board, Wait01(ActionPauseSeconds));
                        yield return Wait(ActionPauseSeconds);
                    }
                    MomentumFireController.OnActionPauseEnd(_board);
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
                    yield return _duelPerf.Play(group, _resolver.Resolve(group), _ctx);
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
                        ApplyGroupSilently(group);
                        yield break;
                    }
                    // 犹豫延迟宣告：台词已在前一 TraitLine 组弹出；此处补飘字「延迟」
                    if (group.Root is SkillTriggerEvent { Kind: "delayed" } delayedSt)
                    {
                        var delayedUnit = _ctx.Unit(delayedSt.ActorId);
                        if (delayedUnit != null)
                            _ctx.Floats.Show(delayedUnit, "延迟", new Color(0.75f, 0.7f, 1f), 1.1f);
                        yield return Wait(profile.DelayedAnnouncePause);
                        ApplyGroupSilently(group);
                        yield break;
                    }
                    // 追伤第 5 次补充门槛（C10）：行动窗内第 5 个追击单元 cut-in
                    if (group.Kind == GroupKind.Pursuit && ++_pursuitCountInWindow == 5)
                    {
                        var pursuer = group.Root is SkillTriggerEvent pst ? pst.ActorId : null;
                        RequestCutIn(pursuer, "追击不止！", group.Root.GroupId);
                    }
                    // 满档 cut-in（2026-07-22 语义修订）：本组会给**已满的轨**再进账
                    // → 出手前阻塞预播 cut-in（独占时间轴，切完才出手），并把本组
                    // 攻击主音效换成强化版（EmpoweredStrike）
                    bool empowered = false;
                    var fullCut = FindFullTrackCutIn(group);
                    if (fullCut != null &&
                        MomentumService.TrackTable.ContainsKey(fullCut.Track))
                    {
                        var cutUnit = _ctx.Unit(fullCut.HeroId);
                        if (cutUnit != null)
                        {
                            // 提示文字 = 该次即将造成伤害的技能名（2026-07-22）
                            yield return CutInService.Ensure().PlaySoloBlocking(
                                _ctx, cutUnit, $"{SkillNameOf(group)}！", fullCut.GroupId);
                            empowered = true;
                            _ctx.EmpoweredStrike = true;
                        }
                    }
                    // 连发演出（B1）：第 2 次起节拍加速 + 计数角标（倍率走 profile 配置）
                    bool burst = group.Root is SkillTriggerEvent { BurstNo: >= 2 };
                    if (burst)
                    {
                        var st = (SkillTriggerEvent)group.Root;
                        _ctx.TempoScale = Mathf.Max(1f, profile.BurstTempoScale);
                        var caster = _ctx.Unit(st.ActorId);
                        if (caster != null)
                            _ctx.Floats.Show(caster, $"连发 ×{st.BurstNo}",
                                new Color(1f, 0.85f, 0.3f), 1.15f);
                    }
                    SkillPerformance performance =
                        profile.Template == PerformanceTemplate.OracleAura ? _oraclePerf : _defaultPerf;
                    yield return performance.Play(group, profile, _ctx);
                    if (burst) _ctx.TempoScale = 1f;
                    if (empowered) _ctx.EmpoweredStrike = false;
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
                    // 自身行动窗开始：四轨势能镜像清零（EventApplyService 统一落账）
                    EventApplyService.Apply(action, _ctx, animated: true);
                    _pursuitCountInWindow = 0;
                    if (unit != null && action.Skipped)
                        _ctx.Floats.Show(unit, "无法行动", new Color(0.7f, 0.7f, 0.8f), 1.0f);
                    break;
                case MomentumChangeEvent momentum: // 独立组根的势能事件（少见）：正常落账
                    EventApplyService.Apply(momentum, _ctx, animated: true);
                    break;
            }
            // 节点组子事件落账；台词已由 TraitLineExtract 抽走，此处不再弹气泡
            foreach (var ev in group.Events)
            {
                if (ReferenceEquals(ev, group.Root)) continue;
                EventApplyService.Apply(ev, _ctx, animated: false);
            }
            yield break;
        }

        // ---------------------------------------------------------- 高光回放（B6/C2）

        /// <summary>终局高光回放：按 HighlightSelector 选出的最佳行动窗整段重播。
        /// 纯客户端二次剪辑：窗前事件静默落账、窗内事件正常演出。</summary>
        public void PlayHighlight(string teamId = "A")
        {
            if (_report == null) { Debug.LogWarning("[ClientBattle] 无战报，先播放一场"); return; }
            if (!HighlightSelector.TryFindBestWindow(_report, teamId, out var window))
            {
                Debug.LogWarning("[ClientBattle] 未找到我方伤害行动窗");
                return;
            }
            StopPlayback();
            CutInService.Ensure().ResetDedup(); // 避免整场播放残留挡住高光窗 cut-in
            _playLoop = StartCoroutine(PlayHighlightLoop(window));
        }

        IEnumerator PlayHighlightLoop(HighlightWindow window)
        {
            IsPlaying = true;
            BuildWorld();
            yield return new WaitUntil(() => _ctx.Vfx.PrewarmComplete);
            SetBanner($"★ 高光回放 — {window.ActorId}（单窗伤害 {window.Damage}）");
            var groups = _pipeline.Run(_report.Games[window.GameIndex].Events);
            yield return PlayGroupsRange(groups, window.StartSeq, window.EndSeq);
            SetBanner($"高光回放结束 — {window.ActorId}");
            FinishPlayback();
        }

        /// <summary>组的技能显示名（满档 cut-in 提示文字用）：主动/追击取战法名、
        /// 普攻「普攻」、状态触发取状态中文名（与飘字 FloatNameOf 同口径）。</summary>
        static string SkillNameOf(EventGroup group) => group.Root switch
        {
            SkillTriggerEvent st => Names.ChineseNames.Skill(st.SkillId),
            NormalAttackEvent => "普攻",
            StatusTickEvent tick => Names.ChineseNames.Status(tick.Status?.StatusId ?? ""),
            _ => "势能全开",
        };

        /// <summary>找出本组内"轨已满后再次进账"的满档 cut-in 事件（出手前预播用）。
        /// 判断基于落账前的镜像值：该轨当前 ≥ Full 且事件带 cut_in——刚满 5 的
        /// 当次不算（语义：该类型满了后，该类型再次伤害才切入）。</summary>
        static MomentumChangeEvent FindFullTrackCutIn(EventGroup group)
        {
            foreach (var ev in group.Events)
                if (ev is MomentumChangeEvent { CutIn: true } m &&
                    MomentumService.ValueOf(m.HeroId, m.Track) >= MomentumService.Full)
                    return m;
            return null;
        }

        /// <summary>行动类播放单元（占用时间轴、结束后加节奏停顿）。</summary>
        static bool IsActionKind(GroupKind kind) =>
            kind is GroupKind.ActiveSkill or GroupKind.NormalAttack or GroupKind.Pursuit
                 or GroupKind.StatusTrigger or GroupKind.Duel;

        // ---------------------------------------------------------- 落账/工具

        void ApplyGroupSilently(EventGroup group)
        {
            foreach (var ev in group.Events)
                EventApplyService.Apply(ev, _ctx, animated: false);
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
            if (CutInService.Instance != null) CutInService.Instance.CancelAll();
        }

        void FinishPlayback()
        {
            IsPlaying = false;
            _playLoop = null;
            if (_report != null)
            {
                _settlement = BattleSkillStatsAggregator.Build(_report);
                SettlementPanel.Ensure().Show(_settlement);
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
                SettlementPanel.Ensure().Show(_settlement);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientBattle] 生成结算表失败: {e}");
            }
        }

        WaitForSeconds Wait(float seconds) =>
            new(seconds * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, Speed));

        float Wait01(float seconds) =>
            seconds * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, Speed);

        void SetBanner(string text) => BannerService.Ensure().Set(text);

        /// <summary>cut-in 请求（转发 CutInService.Request；去重在其内部）。</summary>
        public void RequestCutIn(string heroId, string text, int groupId) =>
            CutInService.Ensure().Request(_ctx, heroId, text, groupId);

        /// <summary>高伤补充门槛判定（SkillPerformance.SettleDamage 回调）；
        /// 文本带伤害额度（2026-07-22）。</summary>
        public void NotifyDamageSettled(DamageEvent damage, string floatName)
        {
            if (damage.Amount > HighDamageCutIn)
                RequestCutIn(damage.SourceId,
                    $"{floatName} 重创 {damage.TargetId}！-{damage.Amount}", damage.GroupId);
        }
    }
}
