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

        /// <summary>结算一条伤害事件的通用表现：命中特效+飘字+掉血+受击顿挫+音效。
        /// 格挡/反弹/闪避（amount 可为 0）仍播出击命中反馈；仅减弱受击顿挫与震屏。</summary>
        protected static void SettleDamage(DamageEvent damage, PerformanceProfile profile,
                                           VFXContext ctx, string floatSkillName)
        {
            var target = ctx.Unit(damage.TargetId);
            if (target == null) return;

            bool mitigated = !string.IsNullOrEmpty(damage.Mitigation);
            // 出击命中帧：有专属 HitKey 时格挡/反弹也要播（「打出去了」）；无则靠 PlayMelee 斩击
            if (!string.IsNullOrEmpty(profile.HitKey))
                ctx.Vfx.PlayAt(profile.HitKey, target.transform.position, ctx.Scaled(0.5f));
            if (!mitigated)
            {
                target.HitReact(damage.IsCrit);
                if (profile.CameraShakeOnHit)
                    ctx.Shake(damage.IsCrit ? 0.2f : 0.08f, 0.22f);
            }
            else
            {
                // 格挡/反弹：轻顿挫，表示打在盾/身上，而非完全无反馈
                target.HitReact(isCrit: false);
                // 普通格挡 / 圣盾反伤：卡面中央渐变闪图标（VFX/ 待上传，现色块占位）
                if (damage.Mitigation == "block")
                    target.FlashOverlayIcon("icon_block",
                        tint: new Color(0.75f, 0.82f, 0.95f), duration: ctx.Scaled(0.65f));
                else if (damage.Mitigation == "reflect")
                    target.FlashOverlayIcon("icon_aegis",
                        tint: new Color(1f, 0.88f, 0.45f), duration: ctx.Scaled(0.7f));
            }
            ctx.Sfx.Play(string.IsNullOrEmpty(profile.HitSfxKey) ? "sfx_hit_default" : profile.HitSfxKey);
            ctx.Floats.ShowDamage(target, floatSkillName, damage.Amount, damage.IsCrit,
                damage.Mitigation, damage.DamageType);
            EventApplyService.ApplyDamage(damage, ctx); // 镜像写入统一入口（R-7.4）
            ctx.OnDamageSettled?.Invoke(damage, floatSkillName);
        }

        /// <summary>结算一条治疗事件：绿字+回血。</summary>
        protected static void SettleHeal(HealEvent heal, VFXContext ctx, string floatSkillName)
        {
            var target = ctx.Unit(heal.TargetId);
            if (target == null) return;
            ctx.Vfx.PlayAt("heal_generic", target.transform.position, ctx.Scaled(0.5f));
            ctx.Sfx.Play("sfx_heal_default");
            ctx.Floats.ShowHeal(target, floatSkillName, heal.Amount, heal.IsCrit);
            EventApplyService.ApplyHeal(heal, ctx); // 镜像写入统一入口（R-7.4）
        }

        /// <summary>组内非伤害/治疗副事件的兜底表现（状态/属性/兵力/阵亡/台词）。
        /// 落账统一走 EventApplyService（animated=true 带飘字/音效）。</summary>
        protected static void SettleSideEvent(BattleEvent ev, VFXContext ctx) =>
            EventApplyService.Apply(ev, ctx, animated: true);

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
