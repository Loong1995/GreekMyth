using System.Collections;
using System.Collections.Generic;
using ClientBattle.Events;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 通用默认演出（client_perform §一 默认策略）：
    //
    // - 群攻主动（选敌数 N≥2）：整战法一个单元——施法者移动至棋盘中心，
    //   释放 N 道刀光/魔法光（按伤害类型）指向被打者，然后掉血。
    // - 非群攻主动：按伤害段数每段为一个播放节拍逐段播放。
    // - 普攻：施法者移动至被打者卡牌近身，命中帧在被打者身上闪斩击（1.0 基准）；
    //   一个普攻一个单元。
    // - 追击：群攻走主动群攻逻辑；单体走普攻逻辑按段数打对应次数，
    //   斩击比普攻大一档（1.5×，另乘 profile.StrikeVfxScale 可调）。
    // - 特殊状态触发：走主动逻辑，但飘字飘状态来源战法名；
    //   （已由 ReactionRegroupProcessor 保证排在其他单元之后播放）。
    // 通用时间轴：施法者前摇特效 →（有弹道则飞向目标）→ 命中帧同帧触发命中
    // 特效 → 如有状态事件则触发 Buff 表现。
    //
    // 零死帧原则（2026-07-10 定）：本文件所有 yield 等待都必须对应一段正在
    // 播放的可见动画（位移 tween / 弹道飞行 / 命中·治疗特效）；禁止"纯定格"
    // WaitForSeconds 垫时长——观感即卡顿。节拍完全由动画时长驱动。
    // =========================================================================

    public class DefaultPerformance : SkillPerformance
    {
        public override IEnumerator Play(EventGroup group, PerformanceProfile profile, VFXContext ctx)
        {
            var damages = group.All<DamageEvent>();
            string actorId = ActorOf(group);
            var actor = ctx.Unit(actorId);
            string floatName = FloatNameOf(group);

            // 台词类副事件先行弹出（性格事件推送即播）
            foreach (var ev in group.Events)
                if (ev is TraitTriggerEvent trait)
                    ctx.Bubbles.Say(ctx.Unit(trait.HeroId), trait.Line);

            var template = profile.Template;
            if (template == PerformanceTemplate.Auto || template == PerformanceTemplate.StatusTrigger)
            {
                int distinctTargets = CountDistinctTargets(damages);
                bool melee = group.Kind == GroupKind.NormalAttack ||
                             (group.Kind == GroupKind.Pursuit && distinctTargets <= 1);
                template = melee ? PerformanceTemplate.Melee
                    : distinctTargets >= 2 ? PerformanceTemplate.AoeCenter
                                           : PerformanceTemplate.PerSegment;
            }

            ctx.Sfx.Play(SfxOf(group, profile));
            if (!string.IsNullOrEmpty(profile.CastKey) && actor != null)
                ctx.Vfx.PlayAt(profile.CastKey, actor.transform.position, ctx.Scaled(0.4f));

            switch (template)
            {
                case PerformanceTemplate.Melee:
                    yield return PlayMelee(group, profile, ctx, actor, damages, floatName);
                    break;
                case PerformanceTemplate.AoeCenter:
                    yield return PlayAoeCenter(group, profile, ctx, actor, damages, floatName);
                    break;
                default:
                    yield return PlayPerSegment(group, profile, ctx, actor, damages, floatName);
                    break;
            }

            // 剩余副事件（状态/属性/兵力/阵亡等）统一收尾表现
            foreach (var ev in group.Events)
                if (ev is not DamageEvent && ev is not TraitTriggerEvent)
                    SettleSideEvent(ev, ctx);

            // 特殊图标（如阿喀琉斯裂甲长矛：在目标身上闪一个超大图标）
            // 异步播完即收，不占节拍——下一组开始时它仍在闪。
            if (!string.IsNullOrEmpty(profile.ExtraIconKey) && damages.Count > 0)
            {
                var lastTarget = ctx.Unit(damages[damages.Count - 1].TargetId);
                if (lastTarget != null)
                {
                    var icon = ctx.Vfx.PlayOn(profile.ExtraIconKey, lastTarget.transform,
                        ctx.Scaled(0.6f), new Vector3(0f, 0.3f, -0.5f));
                    icon.transform.localScale *= profile.ExtraIconScale;
                }
            }
        }

        // ---------------------------------------------------------- 普攻/近身

        IEnumerator PlayMelee(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                              Units.UnitView actor, List<DamageEvent> damages, string floatName)
        {
            // 近身斩击尺寸：普攻 1.0 基准，追击 1.5 倍更醒目；再乘 profile 缩放
            float strikeScale = (group.Kind == GroupKind.Pursuit ? 1.5f : 1.0f)
                                * Mathf.Max(0.05f, profile.StrikeVfxScale);
            string strikeKey = ProjectileKeyOf(profile, damages);
            foreach (var damage in damages)
            {
                var target = ctx.Unit(damage.TargetId);
                if (actor != null && target != null && !actor.Defeated)
                {
                    // 移动至被打者卡牌近身
                    Vector3 strikePos = Vector3.Lerp(target.HomePosition, actor.HomePosition, 0.28f);
                    var dash = actor.transform.DOMove(strikePos, ctx.Scaled(0.22f)).SetEase(Ease.InQuad);
                    yield return dash.WaitForCompletion();
                }
                // 命中帧在被打者身上闪斩击（追击比普攻大一档）
                if (target != null)
                {
                    var strike = ctx.Vfx.PlayAt(strikeKey,
                        target.HomePosition + new Vector3(0f, 0.2f, -0.5f), ctx.Scaled(0.45f));
                    // 在 prefab 自带缩放基础上相乘（直接覆盖会破坏资源归一化尺寸）
                    strike.transform.localScale *= strikeScale;
                }
                SettleDamage(damage, profile, ctx, floatName);
                // 命中即回身：回程位移本身是可见动画，斩击/受击闪烁与其同播，
                // 不许在命中后垫任何定格等待
                if (actor != null && !actor.Defeated)
                {
                    var back = actor.transform.DOMove(actor.HomePosition, ctx.Scaled(0.24f)).SetEase(Ease.OutQuad);
                    yield return back.WaitForCompletion();
                }
            }
        }

        // ---------------------------------------------------------- 群攻主动

        IEnumerator PlayAoeCenter(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                                  Units.UnitView actor, List<DamageEvent> damages, string floatName)
        {
            // 施法者移动至卡盘中心
            if (actor != null && !actor.Defeated)
            {
                var move = actor.transform.DOMove(ctx.BoardCenter, ctx.Scaled(0.3f)).SetEase(Ease.OutQuad);
                yield return move.WaitForCompletion();
            }

            // N 道刀光/魔法光同时指向各被打者 → 命中帧同帧结算掉血
            string projectileKey = ProjectileKeyOf(profile, damages);
            foreach (var damage in damages)
            {
                var target = ctx.UnitTransform(damage.TargetId);
                if (target != null)
                    LaunchProjectile(ctx, projectileKey,
                        actor != null ? actor.transform.position : ctx.BoardCenter,
                        target.position, ctx.Scaled(0.28f));
            }
            yield return new WaitForSeconds(ctx.Scaled(0.28f)); // 弹道飞行（弹道全程可见）
            foreach (var damage in damages)
                SettleDamage(damage, profile, ctx, floatName);
            // 命中特效/受击闪烁与回身位移同播，命中后不垫定格
            if (actor != null && !actor.Defeated)
            {
                var back = actor.transform.DOMove(actor.HomePosition, ctx.Scaled(0.3f)).SetEase(Ease.OutQuad);
                yield return back.WaitForCompletion();
            }
        }

        // ---------------------------------------------------------- 单体逐段

        IEnumerator PlayPerSegment(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                                   Units.UnitView actor, List<DamageEvent> damages, string floatName)
        {
            string projectileKey = ProjectileKeyOf(profile, damages);
            foreach (var damage in damages)
            {
                var target = ctx.UnitTransform(damage.TargetId);
                if (target != null)
                {
                    LaunchProjectile(ctx, projectileKey,
                        actor != null ? actor.transform.position : ctx.BoardCenter,
                        target.position, ctx.Scaled(0.22f));
                    yield return new WaitForSeconds(ctx.Scaled(0.22f)); // 弹道飞行可见
                }
                SettleDamage(damage, profile, ctx, floatName);
                // 段间不垫定格：下一段弹道立刻起飞，受击闪烁/飘字与其同播
            }
            // 纯治疗/纯状态组（无伤害段）：等待覆盖治疗特效可见窗口
            foreach (var heal in group.All<HealEvent>())
            {
                SettleHeal(heal, ctx, floatName);
                yield return new WaitForSeconds(ctx.Scaled(0.3f)); // heal_generic 播放中
            }
        }

        // ---------------------------------------------------------- 工具

        static void LaunchProjectile(VFXContext ctx, string key, Vector3 from, Vector3 to, float flightTime)
        {
            var projectile = ctx.Vfx.PlayAt(key, from, flightTime + 0.05f);
            projectile.transform.DOMove(to, flightTime).SetEase(Ease.InQuad).SetLink(projectile);
        }

        /// <summary>刀光/魔法光按伤害类型选：物理 slash、魔法 magic_bolt（可被特殊配置覆盖）。</summary>
        static string ProjectileKeyOf(PerformanceProfile profile, List<DamageEvent> damages)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileKey)) return profile.ProjectileKey;
            bool magic = damages.Count > 0 && damages[0].DamageType == "magic";
            return magic ? "magic_bolt" : "slash";
        }

        static int CountDistinctTargets(List<DamageEvent> damages)
        {
            var ids = new HashSet<string>();
            foreach (var damage in damages) ids.Add(damage.TargetId);
            return ids.Count;
        }

        static string ActorOf(EventGroup group)
        {
            switch (group.Root)
            {
                case SkillTriggerEvent st: return st.ActorId;
                case NormalAttackEvent na: return na.ActorId;
                case StatusTickEvent tick: return tick.SourceId ?? tick.Status?.OwnerId;
                case DamageEvent damage: return damage.SourceId;
                case HealEvent heal: return heal.SourceId;
                default: return null;
            }
        }

        static string SfxOf(EventGroup group, PerformanceProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.SfxKey)) return profile.SfxKey;
            return group.Kind switch
            {
                GroupKind.NormalAttack => "sfx_melee_default",
                GroupKind.Pursuit => "sfx_pursuit_default",
                GroupKind.StatusTrigger => "sfx_status_trigger_default",
                _ => "sfx_active_default",
            };
        }
    }
}
