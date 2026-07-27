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

        /// <summary>结算一条伤害的命中拍：裂地 + 命中特效 + 受击抖动（及震屏）同一拍起。
        /// 格挡/反弹/闪避（amount 可为 0）仍播出击命中反馈；仅减弱受击顿挫与震屏。
        /// 绕身视觉仍在场时不受击抖动；渐隐后恢复。</summary>
        /// <summary>命中特效存活上限（真实秒）。厂包件窗口不可控，实例不能无限活着
        /// 占池；当前最长件 hit_lightning=2.0s，上限须 ≥ 实测窗口。</summary>
        const float HitVfxWindowCap = 2.5f;

        /// <summary>巨额伤害（触发「重创」横幅，>CutInPolicy.HighDamageThreshold）
        /// 的卡面命中件：RFX4 Effect15_Collision（画廊 3/8 件 7/54 的碰撞子件）。
        /// 解析最高优先级，覆盖一切 Profile 专配（见 ResolveHitKey）。</summary>
        const string MassiveHitKey = "hit_massive";

        /// <summary>巨伤同帧震屏。strength 按「期望世界偏移」计（CameraShaker
        /// 除以 MaxOffset 折 trauma）；须明显高于暴击 0.2，且 MaxOffset 已抬到
        /// 0.75——否则远机位上巨伤与暴击都读作「没震」（P-73）。</summary>
        const float MassiveShakeAmp = 0.55f, MassiveShakeSeconds = 0.48f;

        protected static void SettleDamage(DamageEvent damage, PerformanceProfile profile,
                                           VFXContext ctx, string floatSkillName)
        {
            var target = ctx.Unit(damage.TargetId);
            if (target == null) return;

            bool mitigated = !string.IsNullOrEmpty(damage.Mitigation);
            bool massive = CutInPolicy.IsHighDamage(damage); // 与「重创」横幅同判据同帧
            // —— 命中拍（同帧）：裂地 / HitKey / HitReact(+震屏) 不得拆到模板或错峰 ——
            if (GroundCrackService.ShouldPlayHit(damage))
                GroundCrackService.PlayHit(ctx, profile, target, massive);
            string hitKey = ResolveHitKey(profile, damage);
            if (!string.IsNullOrEmpty(hitKey))
            {
                // 回收时长按件的**发射窗口**给足（真实秒），不再写死 0.5s：
                // Magic 碰撞子件的层要发射 1~2s，0.5s 就收势等于砍掉大半表演，
                // 观感远逊画廊（2026-07-27 实翻车：hit_lightning 窗口 2.0s）。
                // 命中不阻塞时间轴，只是让实例活到自然放完，节拍不受影响。
                float hitLife = Mathf.Max(ctx.Scaled(0.5f),
                    ctx.Vfx.EmitWindow(hitKey, HitVfxWindowCap));
                ctx.Vfx.PlayAt(hitKey, target.transform.position, hitLife);
            }
            // 受击方向取伤害来源的**站位中心**，不是 transform.position——攻击方
            // 突进后就贴在身边，用实时位置算出的击退方向会乱跳甚至反向。
            // 来源不在场（环境/状态伤）时为 null ＝不击退，只给立绘挤压。
            var source = ctx.Unit(damage.SourceId);
            Vector3? fromHome = source != null ? source.HomePosition : null;
            // 巨伤震屏与命中拍同帧，且不吃 CameraShakeOnHit 开关（横幅级反馈）
            if (massive)
                ctx.Shake(MassiveShakeAmp, MassiveShakeSeconds);
            if (!mitigated)
            {
                target.HitReact(damage.IsCrit, fromHome);
                if (!massive && profile.CameraShakeOnHit)
                    ctx.Shake(damage.IsCrit ? 0.2f : 0.08f, 0.22f);
            }
            else
            {
                target.HitReact(isCrit: false, fromHome);
                if (damage.Mitigation == "block")
                    target.FlashOverlayIcon("icon_block",
                        tint: new Color(0.75f, 0.82f, 0.95f), duration: ctx.Scaled(0.65f));
                else if (damage.Mitigation == "reflect")
                    target.FlashOverlayIcon("icon_aegis",
                        tint: new Color(1f, 0.88f, 0.45f), duration: ctx.Scaled(0.7f));
            }
            // —— 命中拍之后：音效 / 飘字 / 镜像兵力 ——
            ctx.Sfx.Play(string.IsNullOrEmpty(profile.HitSfxKey) ? "sfx_hit_default" : profile.HitSfxKey);
            ctx.Floats.ShowDamage(target, floatSkillName, damage.Amount, damage.IsCrit,
                damage.Mitigation, damage.DamageType);
            EventApplyService.ApplyDamage(damage, ctx);
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

        /// <summary>命中特效解析（唯一入口，四级；文档 vfx_config_index.md §一）：
        /// ① 巨伤覆盖——触发「重创」横幅的伤害一律 <c>hit_massive</c>
        ///   （RFX4 Effect15_Collision），压过一切专配；
        /// ② Profile.HitKey 非空（专配战法 / 组默认，如普攻 hit_generic、
        ///   神谕伤害 hit_wave）；
        /// ③ 按 damage_type：魔法 <c>hit_petrify</c>（画廊 1/8 件 41/61）、
        ///   其余 <c>hit_sword</c>（件 45/61）——主动与追击默认都落到这里；
        /// ④ damage 缺失 → <c>hit_generic</c> 兜底。</summary>
        protected static string ResolveHitKey(PerformanceProfile profile, DamageEvent damage)
        {
            if (CutInPolicy.IsHighDamage(damage)) return MassiveHitKey;
            if (profile != null && !string.IsNullOrEmpty(profile.HitKey)) return profile.HitKey;
            if (damage == null) return "hit_generic";
            return damage.DamageType == "magic" ? "hit_petrify" : "hit_sword";
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
