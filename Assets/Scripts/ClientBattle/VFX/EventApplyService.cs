using ClientBattle.Events;
using ClientBattle.Names;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】EventApplyService：全客户端唯一的事件落账入口。
    //
    // - Apply(ev, ctx, animated)：把一条战报事件的权威结果刷进视图镜像
    //   （兵力/状态/光环/石化/势能/属性/阵亡/行动窗清账），零客户端结算。
    // - animated=false（静默：跳过、ParallelWithNext、节点组副事件）只落账；
    //   animated=true 追加轻量反馈（状态飘字/音效、势能白闪、阵亡飘字音效）。
    // - 伤害/治疗的完整演出（命中特效/顿挫/震屏/飘字）仍由
    //   SkillPerformance.SettleDamage/SettleHeal 负责——本服务对 Damage/Heal
    //   只兜底刷兵力（两条路径都以 troops_after 为准，天然幂等）。
    // - cut-in 请求（势能满档/战术变更）统一从这里发出，去重在 CutInService。
    // =========================================================================

    public static class EventApplyService
    {
        public static void Apply(BattleEvent ev, VFXContext ctx, bool animated)
        {
            if (ev == null || ctx == null) return;
            switch (ev)
            {
                case DamageEvent d when d.Troops != null:
                    ctx.Unit(d.TargetId)?.SetTroops(d.Troops.TroopsAfter);
                    break;
                case HealEvent h when h.Troops != null:
                    ctx.Unit(h.TargetId)?.SetTroops(h.Troops.TroopsAfter);
                    break;
                case TroopsChangeEvent t when t.Troops != null:
                    ctx.Unit(t.Troops.HeroId)?.SetTroops(t.Troops.TroopsAfter);
                    break;
                case StatusApplyEvent apply when apply.Status != null:
                    ApplyStatus(apply, ctx, animated);
                    break;
                case StatusRemoveEvent remove when remove.Status != null:
                    RemoveStatus(remove, ctx, animated);
                    break;
                case HeroDefeatedEvent defeated:
                    ApplyDefeated(defeated, ctx, animated);
                    break;
                case AttrChangeEvent attr:
                    // 属性飘字静默路径也播（历史行为：单挑惩罚等在静默节点落账时仍可见）
                    var unit = ctx.Unit(attr.HeroId);
                    foreach (var change in attr.Changes)
                        ctx.Floats.ShowAttr(unit, ChineseNames.Attr(change.Attr),
                            change.After - change.Before);
                    break;
                case MomentumChangeEvent momentum:
                    ApplyMomentum(momentum, ctx, animated);
                    break;
                case TacticAppliedEvent tactic: // 战术变更：无主体，走横幅回退（1.4.1）
                    ctx.OnCutInRequested?.Invoke(null,
                        $"{tactic.TeamId} 队变更战术 →「{ChineseNames.Status(tactic.TacticId)}」",
                        tactic.GroupId);
                    break;
                case ActionStartEvent action:
                    // 自身行动窗开始：四轨势能镜像清零（与服务器静默清零同步）
                    MomentumService.OnActionStart(action.ActorId, ctx.Unit(action.ActorId));
                    break;
                case TraitTriggerEvent:
                    // 台词只走 TraitLine 独占组；落账路径不弹气泡，避免重叠
                    break;
            }
        }

        static void ApplyStatus(StatusApplyEvent apply, VFXContext ctx, bool animated)
        {
            var owner = ctx.Unit(apply.Status.OwnerId);
            if (owner == null) return;
            string statusId = apply.Status.StatusId;
            owner.StatusPanel.AddStatus(statusId);
            UnitAuraService.OnStatusApplied(owner, statusId); // 有配置则挂常驻循环光环
            if (statusId == "petrify") owner.SetPetrified(true);
            if (!animated) return;
            ctx.Floats.ShowStatus(owner, ChineseNames.Status(statusId), gained: true);
            bool isNew = apply is not StatusRefreshEvent;
            if (isNew) ctx.Sfx.Play($"sfx_status_{statusId}"); // 同帧去重由 SfxManager 负责
        }

        static void RemoveStatus(StatusRemoveEvent remove, VFXContext ctx, bool animated)
        {
            var owner = ctx.Unit(remove.Status.OwnerId);
            if (owner == null) return;
            string statusId = remove.Status.StatusId;
            owner.StatusPanel.RemoveStatus(statusId);
            UnitAuraService.OnStatusRemoved(owner, statusId);
            if (statusId == "petrify")
            {
                owner.SetPetrified(false);                       // 石化渐变回来
                if (animated) ctx.Sfx.Play("sfx_petrify_off");   // 石头脱落音效
            }
        }

        static void ApplyDefeated(HeroDefeatedEvent defeated, VFXContext ctx, bool animated)
        {
            var fallen = ctx.Unit(defeated.HeroId);
            if (fallen == null || fallen.Defeated) return;
            fallen.PlayDefeated();
            UnitAuraService.OnUnitDefeated(fallen);
            if (!animated) return;
            ctx.Sfx.Play("sfx_defeated");
            ctx.Floats.Show(fallen, defeated.IsMainHero ? "主将阵亡!" : "阵亡",
                new Color(1f, 0.4f, 0.2f), 1.4f);
        }

        static void ApplyMomentum(MomentumChangeEvent momentum, VFXContext ctx, bool animated)
        {
            // cut-in 语义（2026-07-22 修订）：轨**已满之后**该轨再次进账才切入
            // ——刚满 5 的当次不切（服务端满 5 当次起都带 cut_in，客户端按
            // 落账前镜像值过滤）；其他轨互不影响。动作组内的满档 cut-in 由
            // Runner 在出手前阻塞预播，此处的分发靠同组去重不会重复。
            bool wasFull = MomentumService.ValueOf(momentum.HeroId, momentum.Track)
                           >= MomentumService.Full;
            MomentumService.Apply(momentum, ctx.Unit(momentum.HeroId), silent: !animated);
            if (momentum.CutIn && wasFull &&
                MomentumService.TrackTable.TryGetValue(momentum.Track, out var style))
                ctx.OnCutInRequested?.Invoke(
                    momentum.HeroId, $"势能全开·{style.Label}！", momentum.GroupId);
        }
    }
}
