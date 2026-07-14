using System.Collections;
using ClientBattle.Events;
using ClientBattle.Names;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】SkillPerformance 抽象基类（ScriptableObject）。
    //
    // 派生类实现 Play(group, profile, ctx) 协程编排时间轴；基类提供
    // "结算事件 → 表现"的公共原语（掉血/飘字/状态图标/台词/阵亡），
    // 保证任何模板都不遗漏组内副事件——事件为准，不做客户端结算。
    // =========================================================================

    public abstract class SkillPerformance : ScriptableObject
    {
        /// <summary>播放一个 EventGroup 的演出。协程结束 = 本播放单元完成。</summary>
        public abstract IEnumerator Play(EventGroup group, PerformanceProfile profile, VFXContext ctx);

        // ---------------------------------------------------------- 公共原语

        /// <summary>结算一条伤害事件的通用表现：命中特效+飘字+掉血+受击顿挫+音效。</summary>
        protected static void SettleDamage(DamageEvent damage, PerformanceProfile profile,
                                           VFXContext ctx, string floatSkillName)
        {
            var target = ctx.Unit(damage.TargetId);
            if (target == null) return;

            bool mitigated = !string.IsNullOrEmpty(damage.Mitigation);
            if (!mitigated)
            {
                if (!string.IsNullOrEmpty(profile.HitKey))
                    ctx.Vfx.PlayAt(profile.HitKey, target.transform.position, ctx.Scaled(0.5f));
                target.HitReact(damage.IsCrit);
                if (profile.CameraShakeOnHit)
                    ctx.Shake(damage.IsCrit ? 0.2f : 0.08f, 0.22f);
            }
            ctx.Sfx.Play(string.IsNullOrEmpty(profile.HitSfxKey) ? "sfx_hit_default" : profile.HitSfxKey);
            ctx.Floats.ShowDamage(target, floatSkillName, damage.Amount, damage.IsCrit,
                damage.Mitigation, damage.DamageType);
            if (damage.Troops != null)
                target.SetTroops(damage.Troops.TroopsAfter);
        }

        /// <summary>结算一条治疗事件：绿字+回血。</summary>
        protected static void SettleHeal(HealEvent heal, VFXContext ctx, string floatSkillName)
        {
            var target = ctx.Unit(heal.TargetId);
            if (target == null) return;
            ctx.Vfx.PlayAt("heal_generic", target.transform.position, ctx.Scaled(0.5f));
            ctx.Sfx.Play("sfx_heal_default");
            ctx.Floats.ShowHeal(target, floatSkillName, heal.Amount, heal.IsCrit);
            if (heal.Troops != null)
                target.SetTroops(heal.Troops.TroopsAfter);
        }

        /// <summary>组内非伤害/治疗副事件的兜底表现（状态/属性/兵力/阵亡/台词）。</summary>
        protected static void SettleSideEvent(BattleEvent ev, VFXContext ctx)
        {
            switch (ev)
            {
                case StatusApplyEvent apply:   // 含 StatusRefreshEvent（派生）
                    ApplyStatusVisual(apply, ctx, ev is not StatusRefreshEvent);
                    break;
                case StatusRemoveEvent remove:
                    RemoveStatusVisual(remove, ctx);
                    break;
                case AttrChangeEvent attr:
                    var unit = ctx.Unit(attr.HeroId);
                    foreach (var change in attr.Changes)
                        ctx.Floats.ShowAttr(unit, ChineseNames.Attr(change.Attr), change.After - change.Before);
                    break;
                case TroopsChangeEvent troops when troops.Troops != null:
                    ctx.Unit(troops.Troops.HeroId)?.SetTroops(troops.Troops.TroopsAfter);
                    break;
                case HeroDefeatedEvent defeated:
                    var fallen = ctx.Unit(defeated.HeroId);
                    if (fallen != null)
                    {
                        fallen.PlayDefeated();
                        UnitAuraService.OnUnitDefeated(fallen);
                        ctx.Sfx.Play("sfx_defeated");
                        ctx.Floats.Show(fallen, defeated.IsMainHero ? "主将阵亡!" : "阵亡",
                            new Color(1f, 0.4f, 0.2f), 1.4f);
                    }
                    break;
                case TraitTriggerEvent trait:  // 性格台词：推送即播（聊天气泡）
                    ctx.Bubbles.Say(ctx.Unit(trait.HeroId), trait.Line);
                    break;
            }
        }

        static void ApplyStatusVisual(StatusApplyEvent apply, VFXContext ctx, bool isNew)
        {
            var owner = ctx.Unit(apply.Status?.OwnerId);
            if (owner == null) return;
            string statusId = apply.Status.StatusId;
            owner.StatusPanel.AddStatus(statusId);
            UnitAuraService.OnStatusApplied(owner, statusId); // 有配置则挂常驻循环光环
            ctx.Floats.ShowStatus(owner, ChineseNames.Status(statusId), gained: true);
            if (isNew) ctx.Sfx.Play($"sfx_status_{statusId}"); // 同帧与伤害音效由 SfxManager 去重
            if (statusId == "petrify") owner.SetPetrified(true);
        }

        static void RemoveStatusVisual(StatusRemoveEvent remove, VFXContext ctx)
        {
            var owner = ctx.Unit(remove.Status?.OwnerId);
            if (owner == null) return;
            string statusId = remove.Status.StatusId;
            owner.StatusPanel.RemoveStatus(statusId);
            UnitAuraService.OnStatusRemoved(owner, statusId);
            if (statusId == "petrify")
            {
                owner.SetPetrified(false);              // 石化渐变回来
                ctx.Sfx.Play("sfx_petrify_off");        // 石头脱落音效
            }
        }

        /// <summary>取组内飘字用的战法名：状态触发飘"状态来源的战法名"（用状态中文名等价表达）。</summary>
        protected static string FloatNameOf(EventGroup group)
        {
            switch (group.Root)
            {
                case SkillTriggerEvent st: return ChineseNames.Skill(st.SkillId);
                case NormalAttackEvent: return "普攻";
                case StatusTickEvent tick: return ChineseNames.Status(tick.Status?.StatusId ?? "");
                default: return "";
            }
        }
    }
}
