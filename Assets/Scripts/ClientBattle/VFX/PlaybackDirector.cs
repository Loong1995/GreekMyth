using System.Collections;
using System.Collections.Generic;
using ClientBattle.Events;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】PlaybackDirector：主循环与组分派（无生命周期职责）。
    //
    // - PlaySeries：逐局（局间镜像重置 + 横幅）→ 管线 → PlayGroupsRange。
    // - PlayGroupsRange：主循环与高光回放共用（高光 = 带 [start,end) 窗口的
    //   二次剪辑，窗前静默落账）。
    // - PlayGroup：Node / TraitLine / Duel / 演出模板分派；满档 cut-in、
    //   连发节拍、追击门槛（策略数值在 CutInPolicy）。
    // - 只经 PlaybackSession 与 IPlaybackPacing 工作；不 Ensure 单例、
    //   不 new GameObject、不管协程宿主（协程由控制器启动）。
    // =========================================================================

    public class PlaybackDirector
    {
        readonly IPlaybackPacing _pacing;

        public PlaybackDirector(IPlaybackPacing pacing) => _pacing = pacing;

        // ---------------------------------------------------------- 系列/局

        public IEnumerator PlaySeries(PlaybackSession s)
        {
            for (int gameIdx = 0; gameIdx < s.Report.Games.Count; gameIdx++)
            {
                var game = s.Report.Games[gameIdx];
                if (gameIdx > 0)
                {
                    UnitAuraService.ClearAll(); // 整局光环随局重置
                    MomentumService.ClearAll(); // 势能账本随局重置
                    s.Board.ResetForNewGame();
                }
                s.Ctx.OnBanner?.Invoke($"第 {game.GameNo} 局");

                var groups = s.Pipeline.Run(game.Events);
                yield return PlayGroupsRange(s, groups, 0, int.MaxValue);

                s.Ctx.OnBanner?.Invoke(game.WinnerTeamId != null
                    ? $"第 {game.GameNo} 局结束 — {game.WinnerTeamId} 队胜（{game.Reason}）"
                    : $"第 {game.GameNo} 局结束 — 平局（{game.Reason}）");
            }
            s.Ctx.OnBanner?.Invoke(s.Report.SeriesWinnerTeamId != null
                ? $"系列结束 — 胜者 {s.Report.SeriesWinnerTeamId} 队"
                : "系列结束 — 平局");
        }

        /// <summary>按序播放 [startSeq, endSeq) 内的组；范围外前缀静默落账。</summary>
        public IEnumerator PlayGroupsRange(PlaybackSession s, List<EventGroup> groups,
                                           int startSeq, int endSeq)
        {
            bool actedSinceActionStart = false;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                if (group.Root.Seq >= endSeq) break;
                if (group.Root.Seq < startSeq)
                {
                    ApplyGroupSilently(s, group);
                    continue;
                }
                s.Ctx.SpeedScale = _pacing.Speed;
                s.Ctx.DurationMul = _pacing.DurationMul;
                if (group.ParallelWithNext)
                {
                    ApplyGroupSilently(s, group);
                    continue;
                }
                // 势能火相位信号：回合横幅前提前开渐灭（末位行动→下回合之间
                // 往往没有立刻 ActionStart）
                if (group.Root is RoundStartEvent && actedSinceActionStart)
                    MomentumFireController.OnRoundBanner(
                        s.Board, Wait01(Mathf.Max(0.2f, _pacing.ActionPauseSeconds)));
                // 上一行动窗已打出过行动类单元 → 进入下一 action_start 前 ActionPause：
                // 场上势能火在此停顿内渐灭（不依赖 lastActionActor；避免借刀/响应归账漏灭）
                if (group.Root is ActionStartEvent && actedSinceActionStart)
                {
                    actedSinceActionStart = false;
                    if (_pacing.ActionPauseSeconds > 0f)
                    {
                        MomentumFireController.OnActionPauseBegin(
                            s.Board, Wait01(_pacing.ActionPauseSeconds));
                        yield return Wait(_pacing.ActionPauseSeconds);
                    }
                    MomentumFireController.OnActionPauseEnd(s.Board);
                }
                yield return PlayGroup(s, group);
                if (IsActionKind(group.Kind))
                {
                    actedSinceActionStart = true;
                    // 下一组是台词：不加单元停顿（台词独占单元与邻组无缝衔接）
                    bool nextTrait = gi + 1 < groups.Count
                                     && groups[gi + 1].Kind == GroupKind.TraitLine
                                     && !groups[gi + 1].ParallelWithNext;
                    if (_pacing.GroupPauseSeconds > 0f && !nextTrait)
                        yield return Wait(_pacing.GroupPauseSeconds);
                }
                // TraitLine 本身不加 GroupPause，播完立刻接下一段
            }
        }

        // ---------------------------------------------------------- 组分派

        IEnumerator PlayGroup(PlaybackSession s, EventGroup group)
        {
            var ctx = s.Ctx;
            switch (group.Kind)
            {
                case GroupKind.Node:
                    yield return PlayNode(s, group);
                    break;
                case GroupKind.TraitLine:
                    // 独占：等气泡完整收起后立刻下一组（无 GroupPause）。
                    // SayExclusive 已按 DurationMul/Speed 缩放动画与返回值，勿再 Wait() 二次相乘。
                    foreach (var ev in group.All<TraitTriggerEvent>())
                    {
                        float hold = ctx.Bubbles.SayExclusive(
                            ctx.Unit(ev.HeroId), ev.Line, ctx.DurationMul, ctx.SpeedScale);
                        if (hold > 0f) yield return new WaitForSeconds(hold);
                    }
                    break;
                case GroupKind.Duel:
                    yield return s.DuelPerf.Play(group, s.Resolver.Resolve(group), ctx);
                    break;
                case GroupKind.Defeat:
                case GroupKind.StatusChange:
                    // 非行动组：即时落账（含阵亡倒下/状态图标/光环），不占时间轴
                    ApplyGroupSilently(s, group);
                    break;
                default:
                    var profile = s.Resolver.Resolve(group);
                    if (profile.Template == PerformanceTemplate.None)
                    {
                        ApplyGroupSilently(s, group);
                        yield break;
                    }
                    // 犹豫延迟宣告：台词已在前一 TraitLine 组弹出；此处补飘字「延迟」
                    if (group.Root is SkillTriggerEvent { Kind: "delayed" } delayedSt)
                    {
                        var delayedUnit = ctx.Unit(delayedSt.ActorId);
                        if (delayedUnit != null)
                            ctx.Floats.Show(delayedUnit, "延迟", new Color(0.75f, 0.7f, 1f), 1.1f);
                        yield return Wait(profile.DelayedAnnouncePause);
                        ApplyGroupSilently(s, group);
                        yield break;
                    }
                    // 追伤第 5 次补充门槛（C10）：行动窗内第 5 个追击单元 cut-in
                    if (group.Kind == GroupKind.Pursuit
                        && ++s.PursuitCountInWindow == CutInPolicy.PursuitCutInAt)
                    {
                        var pursuer = group.Root is SkillTriggerEvent pst ? pst.ActorId : null;
                        ctx.OnCutInRequested?.Invoke(pursuer, "追击不止！", group.Root.GroupId);
                    }
                    // 满档 cut-in：该武将某轨已满（≥5）后，本组同轨再次进账
                    // → 出手前阻塞 cut-in，标题 = 即将造成伤害的技能名
                    bool empowered = false;
                    var fullCut = CutInPolicy.FindFullTrackCutIn(group);
                    if (fullCut != null)
                    {
                        var cutUnit = ctx.Unit(fullCut.HeroId);
                        if (cutUnit != null)
                        {
                            string skillTitle = $"{CutInPolicy.SkillNameOf(group)}！";
                            yield return ctx.CutIns.PlaySoloBlocking(
                                ctx, cutUnit, skillTitle, fullCut.GroupId);
                            empowered = true;
                            ctx.EmpoweredStrike = true;
                        }
                    }
                    // 连发演出（B1）：第 2 次起节拍加速 + 计数角标（倍率走 profile 配置）
                    bool burst = group.Root is SkillTriggerEvent { BurstNo: >= 2 };
                    if (burst)
                    {
                        var st = (SkillTriggerEvent)group.Root;
                        ctx.TempoScale = Mathf.Max(1f, profile.BurstTempoScale);
                        var caster = ctx.Unit(st.ActorId);
                        if (caster != null)
                            ctx.Floats.Show(caster, $"连发 ×{st.BurstNo}",
                                new Color(1f, 0.85f, 0.3f), 1.15f);
                    }
                    SkillPerformance performance =
                        profile.Template == PerformanceTemplate.OracleAura ? s.OraclePerf : s.DefaultPerf;
                    yield return performance.Play(group, profile, ctx);
                    if (burst) ctx.TempoScale = 1f;
                    if (empowered) ctx.EmpoweredStrike = false;
                    break;
            }
        }

        IEnumerator PlayNode(PlaybackSession s, EventGroup group)
        {
            var ctx = s.Ctx;
            switch (group.Root)
            {
                case RoundStartEvent round when round.RoundNo > 0:
                    ctx.OnBanner?.Invoke($"第 {round.RoundNo} 回合");
                    break;
                case ActionStartEvent action:
                    var unit = ctx.Unit(action.ActorId);
                    // 自身行动窗开始：四轨势能镜像清零（EventApplyService 统一落账）
                    EventApplyService.Apply(action, ctx, animated: true);
                    s.PursuitCountInWindow = 0;
                    if (unit != null && action.Skipped)
                        ctx.Floats.Show(unit, "无法行动", new Color(0.7f, 0.7f, 0.8f), 1.0f);
                    break;
                case MomentumChangeEvent momentum: // 独立组根的势能事件（少见）：正常落账
                    EventApplyService.Apply(momentum, ctx, animated: true);
                    break;
            }
            // 节点组子事件落账；台词已由 TraitLineExtract 抽走，此处不再弹气泡
            foreach (var ev in group.Events)
            {
                if (ReferenceEquals(ev, group.Root)) continue;
                EventApplyService.Apply(ev, ctx, animated: false);
            }
            yield break;
        }

        // ---------------------------------------------------------- 工具

        public static void ApplyGroupSilently(PlaybackSession s, EventGroup group)
        {
            foreach (var ev in group.Events)
                EventApplyService.Apply(ev, s.Ctx, animated: false);
        }

        /// <summary>行动类播放单元（占用时间轴、结束后加节奏停顿）。</summary>
        static bool IsActionKind(GroupKind kind) =>
            kind is GroupKind.ActiveSkill or GroupKind.NormalAttack or GroupKind.Pursuit
                 or GroupKind.StatusTrigger or GroupKind.Duel;

        WaitForSeconds Wait(float seconds) => new(Wait01(seconds));

        float Wait01(float seconds) =>
            seconds * Mathf.Max(0.1f, _pacing.DurationMul) / Mathf.Max(0.1f, _pacing.Speed);
    }
}
