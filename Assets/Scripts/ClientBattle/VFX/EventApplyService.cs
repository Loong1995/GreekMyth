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
    // - 伤害/治疗的**表现**（命中特效/顿挫/震屏/飘字）由
    //   SkillPerformance.SettleDamage/SettleHeal 编排，但镜像写入（刷兵力）
    //   统一回到本服务 ApplyDamage/ApplyHeal——静默与演出两条路径写账同源，
    //   Skip/重播终态天然一致（R-7.4 单一落账入口）。
    // - cut-in：满档由 Director 出手前阻塞预播（技能名）；高伤/追击5/战术变更仍经本服务 Request。
    // =========================================================================

    public static class EventApplyService
    {
        /// <summary>伤害镜像写入唯一实现（演出路径 SettleDamage 与静默路径共用）。</summary>
        public static void ApplyDamage(DamageEvent d, VFXContext ctx)
        {
            if (d?.Troops == null || ctx == null) return;
            ctx.Unit(d.TargetId)?.SetTroops(d.Troops.TroopsAfter);
        }

        /// <summary>治疗镜像写入唯一实现（演出路径 SettleHeal 与静默路径共用）。</summary>
        public static void ApplyHeal(HealEvent h, VFXContext ctx)
        {
            if (h?.Troops == null || ctx == null) return;
            ctx.Unit(h.TargetId)?.SetTroops(h.Troops.TroopsAfter);
        }

        public static void Apply(BattleEvent ev, VFXContext ctx, bool animated)
        {
            if (ev == null || ctx == null) return;
            switch (ev)
            {
                case DamageEvent d:
                    ApplyDamage(d, ctx);
                    break;
                case HealEvent h:
                    ApplyHeal(h, ctx);
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
                case ActionStartEvent:
                    // 势能按回合清零，action_start 不再动四轨镜像
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
            // 满档 cut-in 不在此处发：由 PlaybackDirector 在出手前阻塞预播
            // （CutInPlanner 编译期判定，文案 = 即将造成
            // 伤害的技能名）。此处只落账 + 条/火表现。
            // （服务端 value≥5 起都带 cut_in；刚满 5 当次不切由该判定按镜像过滤。）
            MomentumService.Apply(momentum, ctx.Unit(momentum.HeroId), silent: !animated);
        }
    }
}
