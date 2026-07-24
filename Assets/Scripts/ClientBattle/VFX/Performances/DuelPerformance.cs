using System;
using System.Collections;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】DuelPerformance：单挑播放单元（阻塞时间轴）。
    //
    // 流程：压暗非参战单位（渐变）→ 号角横幅 → 叫阵台词 →（拒战：拒战台词；应战：
    // 应战台词 → 全屏裂缝交错 cut-in ×clash_cutins → 胜者横幅 → 败者四维
    // 惩罚落账）→ 解除压暗（渐变）。原嵌在 PerformanceRunner.PlayDuel，2026-07-22 拆出。
    // =========================================================================

    public class DuelPerformance : SkillPerformance
    {
        public override IEnumerator Play(EventGroup group, PerformanceProfile profile, VFXContext ctx)
        {
            var challenge = group.First<DuelChallengeEvent>();
            var result = group.First<DuelResultEvent>();
            if (challenge == null) yield break;

            // 全场非对阵双方微微发灰（渐变）；对阵双方强制恢复正常亮度
            string duelA = challenge.ChallengerId, duelB = challenge.DefenderId;
            foreach (var unit in ctx.Board.AllUnits)
            {
                bool duelists = unit.Hero.HeroId == duelA || unit.Hero.HeroId == duelB;
                unit.SetDimmed(!duelists, Units.UnitView.DimFadeSeconds);
            }
            ctx.OnBanner?.Invoke(
                $"⚔ 单挑！{challenge.ChallengerId}（武{challenge.ChallengerForce}） vs " +
                $"{challenge.DefenderId}（武{challenge.DefenderForce}）");
            ctx.Sfx.Play("sfx_duel_horn");
            ctx.OnBgmDuck?.Invoke(); // 单挑全层 duck（B3）；经 ctx 注入，不抓单例
            yield return WaitScaled(ctx, Units.UnitView.DimFadeSeconds);
            yield return WaitScaled(ctx, 0.55f);

            // 叫阵台词（effect=duel_challenge，挂在 challenge 下）
            yield return PlayDuelLines(group, ctx, "duel_challenge");

            if (result != null && !result.Accepted)
            {
                ctx.OnBanner?.Invoke("对方拒绝了单挑");
                yield return PlayDuelLines(group, ctx, "duel_reject");
                yield return WaitScaled(ctx, 0.5f);
            }
            else if (result != null)
            {
                yield return PlayDuelLines(group, ctx, "duel_accept");
                // 全屏裂缝交错 cut-in：两张半屏卡对向滑过中央裂缝线算一次交错，
                // 段数由服务端 clash_cutins 下发（武力越接近交错越多、越快）
                var a = ctx.Unit(challenge.ChallengerId);
                var b = ctx.Unit(challenge.DefenderId);
                int clashes = Math.Clamp(challenge.ClashCutins, 1, 3);
                if (a != null && b != null && ctx.CutIns != null)
                    yield return ctx.CutIns.DuelClashRoutine(
                        ctx, a, b, clashes,
                        onClash: () =>
                        {
                            ctx.Sfx.Play("sfx_duel_clash");
                            ctx.Shake(0.22f, 0.2f);
                        });
                ctx.OnBanner?.Invoke($"单挑胜者：{result.WinnerId}！");
                ctx.Sfx.Play("sfx_duel_win");
                yield return WaitScaled(ctx, 0.8f);
                // 败者四维惩罚等副事件
                foreach (var ev in group.Events)
                    if (ev is AttrChangeEvent) EventApplyService.Apply(ev, ctx, animated: true);
                yield return WaitScaled(ctx, 0.5f);
            }
            foreach (var unit in ctx.Board.AllUnits)
                unit.SetDimmed(false, Units.UnitView.DimFadeSeconds);
            yield return WaitScaled(ctx, Units.UnitView.DimFadeSeconds);
            ctx.OnBanner?.Invoke("");
        }

        /// <summary>单挑组内按 effect 播台词气泡（独占时长）。</summary>
        IEnumerator PlayDuelLines(EventGroup group, VFXContext ctx, string effect)
        {
            foreach (var ev in group.All<TraitTriggerEvent>())
            {
                if (ev.Effect != effect) continue;
                if (string.IsNullOrEmpty(ev.Line)) continue;
                float hold = ctx.Bubbles.SayExclusive(
                    ctx.Unit(ev.HeroId), ev.Line, ctx.DurationMul, ctx.SpeedScale);
                if (hold > 0f) yield return new WaitForSeconds(hold);
            }
        }

        static WaitForSeconds WaitScaled(VFXContext ctx, float seconds) =>
            new(seconds * Mathf.Max(0.1f, ctx.DurationMul) / Mathf.Max(0.1f, ctx.SpeedScale));
    }
}
