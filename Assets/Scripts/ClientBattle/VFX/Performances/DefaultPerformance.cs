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
    //   释放 N 道弹道（物理 blade_bolt / 魔法 magic_bolt）弧线飞向被打者，然后掉血。
    // - 非群攻主动：按伤害段数每段为一个播放节拍逐段播放。
    // - 普攻：施法者移动至被打者卡牌近身，命中帧在被打者身上闪斩击（1.0 基准）；
    //   一个普攻一个单元。
    // - 追击：群攻走主动群攻逻辑；单体走普攻逻辑按段数打对应次数，
    //   斩击比普攻大一档（1.5×，另乘 profile.StrikeVfxScale 可调）。
    // - 特殊状态触发：走主动逻辑，但飘字飘状态来源战法名；
    //   （已由 ReactionRegroupProcessor 保证排在其他单元之后播放；
    //    多方响应序=事件流=引擎先守后攻）。
    // - 远程落击（RemoteStrike）：施法者不位移，目标头顶头像标+落雷。
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

            // 台词由 TraitLineExtractProcessor 抽成独占 TraitLine 组，此处不播

            // 协击标（B1）：队友普攻后的追加协击，出手前挂「协击」角标
            if (group.Root is NormalAttackEvent { Kind: "coordinated" } && actor != null)
                ctx.Floats.Show(actor, "协击", new Color(0.5f, 0.85f, 1f), 1.1f);

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

            // 满档 cut-in 后的出手（势能全开）：主音效换强化版
            ctx.Sfx.Play(ctx.EmpoweredStrike ? "sfx_attack_empowered" : SfxOf(group, profile));
            if (!string.IsNullOrEmpty(profile.CastKey) && actor != null)
            {
                ctx.Vfx.PlayAt(profile.CastKey, actor.transform.position, ctx.Scaled(0.4f));
                // 圣盾等：Cast 闪光需可见一拍再突进（动画时长驱动，非空定格）
                if (template == PerformanceTemplate.Melee)
                    yield return new WaitForSeconds(ctx.Scaled(0.22f));
            }

            switch (template)
            {
                case PerformanceTemplate.Melee:
                    yield return PlayMelee(group, profile, ctx, actor, damages, floatName);
                    break;
                case PerformanceTemplate.AoeCenter:
                    yield return PlayAoeCenter(group, profile, ctx, actor, damages, floatName);
                    break;
                case PerformanceTemplate.RemoteStrike:
                    yield return PlayRemoteStrike(group, profile, ctx, damages, floatName);
                    break;
                default:
                    yield return PlayPerSegment(group, profile, ctx, actor, damages, floatName);
                    break;
            }

            // 剩余副事件（状态/属性/兵力/阵亡等）统一收尾表现
            foreach (var ev in group.Events)
                if (ev is not DamageEvent && ev is not TraitTriggerEvent)
                    SettleSideEvent(ev, ctx);

            // 头像标（B5 皇卡 C1）：受影响单位（伤害目标/属性变化承受者，除施法者外）
            // 头顶浮现指定武将头像——宙斯落雷 zeus、哈迪斯吸统 hades
            // RemoteStrike 已在落雷节拍内挂过，此处不再重复
            if (!string.IsNullOrEmpty(profile.PortraitMarkKey)
                && profile.Template != PerformanceTemplate.RemoteStrike)
            {
                var marked = new HashSet<string>();
                foreach (var damage in damages)
                    if (damage.TargetId != actorId) marked.Add(damage.TargetId);
                foreach (var ev in group.Events)
                    if (ev is AttrChangeEvent attr && attr.HeroId != actorId)
                        marked.Add(attr.HeroId);
                foreach (var id in marked)
                    ctx.Unit(id)?.ShowPortraitMark(profile.PortraitMarkKey);
            }

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
            // 近身斩击尺寸：普攻 1.0；追击/状态追伤突进 1.5；再乘 profile 缩放
            // 借刀（帕特洛克勒斯）：按普攻基准，不因 StatusTrigger 组放大
            float strikeScale = profile.BorrowBlade ? Mathf.Max(0.05f, profile.StrikeVfxScale)
                : (group.Kind is GroupKind.Pursuit or GroupKind.StatusTrigger ? 1.5f : 1.0f)
                  * Mathf.Max(0.05f, profile.StrikeVfxScale);
            string strikeKey = StrikeKeyOf(profile, damages);
            foreach (var damage in damages)
            {
                // 借刀：出击者 = 伤害来源武将；否则 = 组根施法者
                var striker = profile.BorrowBlade ? ctx.Unit(damage.SourceId) : actor;
                var target = ctx.Unit(damage.TargetId);
                if (striker != null && target != null && !striker.Defeated)
                {
                    Vector3 strikePos = Vector3.Lerp(target.HomePosition, striker.HomePosition, 0.28f);
                    var dash = striker.transform.DOMove(strikePos, ctx.Scaled(0.22f)).SetEase(Ease.InQuad);
                    yield return dash.WaitForCompletion();
                }
                if (target != null)
                {
                    var strike = ctx.Vfx.PlayAt(strikeKey,
                        target.HomePosition + new Vector3(0f, 0.2f, -0.5f), ctx.Scaled(0.45f));
                    strike.transform.localScale *= strikeScale;
                }
                SettleDamage(damage, profile, ctx, floatName);
                if (striker != null && !striker.Defeated)
                {
                    var back = striker.transform.DOMove(striker.HomePosition, ctx.Scaled(0.24f)).SetEase(Ease.OutQuad);
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

            // N 道弹道错峰起飞、同时段抵达 → 命中帧同帧结算掉血
            string projectileKey = ProjectileKeyOf(profile, damages);
            Vector3 from = actor != null ? actor.transform.position : ctx.BoardCenter;
            float flightBase = ctx.Scaled(0.38f);
            float stagger = damages.Count > 1 ? ctx.Scaled(0.045f) : 0f;
            for (int i = 0; i < damages.Count; i++)
            {
                var target = ctx.UnitTransform(damages[i].TargetId);
                if (target == null) continue;
                float delay = i * stagger;
                LaunchProjectile(ctx, projectileKey, from, target.position,
                    flightBase - delay, delay);
            }
            yield return new WaitForSeconds(flightBase); // 弹道飞行（弹道全程可见）
            foreach (var damage in damages)
                SettleDamage(damage, profile, ctx, floatName);
            // 命中特效/受击闪烁与回身位移同播，命中后不垫定格
            if (actor != null && !actor.Defeated)
            {
                var back = actor.transform.DOMove(actor.HomePosition, ctx.Scaled(0.3f)).SetEase(Ease.OutQuad);
                yield return back.WaitForCompletion();
            }
        }

        // ---------------------------------------------------------- 远程落击（雷霆）

        IEnumerator PlayRemoteStrike(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                                     List<DamageEvent> damages, string floatName)
        {
            // 触发：Digital Ruby 闪电贯穿对面整张卡（上→下，透明度 0.2）
            float strikeTime = ctx.Scaled(0.35f);
            foreach (var damage in damages)
            {
                var target = ctx.Unit(damage.TargetId);
                if (target == null) continue;
                if (!string.IsNullOrEmpty(profile.PortraitMarkKey))
                    target.ShowPortraitMark(profile.PortraitMarkKey, duration: ctx.Scaled(1.6f));
            }
            yield return new WaitForSeconds(ctx.Scaled(0.06f));
            foreach (var damage in damages)
            {
                var target = ctx.Unit(damage.TargetId);
                if (target == null) continue;
                var center = target.HomePosition;
                // FrameSlotH≈2.3：从卡顶上方劈穿到卡底下方
                var from = center + new Vector3(Random.Range(-0.08f, 0.08f), 1.35f, -0.55f);
                var to = center + new Vector3(Random.Range(-0.12f, 0.12f), -1.35f, -0.55f);
                var bolt = DrLightningUtil.Spawn(ctx.Vfx.transform, "StrikeBolt");
                DrLightningUtil.Fire(bolt, from, to, duration: strikeTime, chaos: 0.22f,
                                     generations: 6, alpha: 0.2f, widthMul: 0.7f, sortingOrder: 50);
                Object.Destroy(bolt.gameObject, strikeTime + 0.05f);
                if (!string.IsNullOrEmpty(profile.HitKey))
                    ctx.Vfx.PlayAt(profile.HitKey, to + new Vector3(0f, 0.1f, 0.1f), ctx.Scaled(0.35f));
            }
            yield return new WaitForSeconds(strikeTime);
            foreach (var damage in damages)
                SettleDamage(damage, profile, ctx, floatName);
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
                    float flight = ctx.Scaled(0.30f);
                    LaunchProjectile(ctx, projectileKey,
                        actor != null ? actor.transform.position : ctx.BoardCenter,
                        target.position, flight);
                    yield return new WaitForSeconds(flight); // 弹道飞行可见
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

        /// <summary>弹道：朝向飞行、二次贝塞尔微弧、出生缩放；可选起飞错峰（群攻齐射）。</summary>
        static void LaunchProjectile(VFXContext ctx, string key, Vector3 from, Vector3 to,
                                     float flightTime, float launchDelay = 0f)
        {
            float life = flightTime + launchDelay + 0.1f;
            var projectile = ctx.Vfx.PlayAt(key, from, life);
            var t = projectile.transform;
            DOTween.Kill(t);

            var stamp = projectile.GetComponent<VfxOriginalScale>();
            Vector3 baseScale = stamp != null ? stamp.Value : Vector3.one;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            float arc = Mathf.Clamp(dist * 0.2f, 0.28f, 0.9f);
            // 弧高朝棋盘上方，略压向镜头，避免贴在卡面上「拖过去」
            Vector3 mid = Vector3.Lerp(from, to, 0.48f)
                          + new Vector3(0f, arc, -0.12f);

            static float FaceZ(Vector3 a, Vector3 b)
            {
                Vector3 d = b - a;
                if (d.sqrMagnitude < 1e-8f) return 0f;
                return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }

            t.position = from;
            t.rotation = Quaternion.Euler(0f, 0f, FaceZ(from, mid));
            t.localScale = launchDelay > 0f ? Vector3.zero : baseScale * 0.4f;

            var seq = DOTween.Sequence().SetLink(projectile);
            if (launchDelay > 0f)
            {
                seq.AppendInterval(launchDelay);
                seq.AppendCallback(() =>
                {
                    t.position = from;
                    t.rotation = Quaternion.Euler(0f, 0f, FaceZ(from, mid));
                    t.localScale = baseScale * 0.4f;
                });
            }

            seq.Append(DOTween.To(() => 0f, u =>
            {
                float o = 1f - u;
                Vector3 p = o * o * from + 2f * o * u * mid + u * u * to;
                Vector3 prev = t.position;
                t.position = p;
                if ((p - prev).sqrMagnitude > 1e-8f)
                    t.rotation = Quaternion.Euler(0f, 0f, FaceZ(prev, p));
                // 前半放大、后半略收，命中前更有「砸入」感
                float s = u < 0.55f
                    ? Mathf.Lerp(0.4f, 1.08f, u / 0.55f)
                    : Mathf.Lerp(1.08f, 0.72f, (u - 0.55f) / 0.45f);
                t.localScale = baseScale * s;
            }, 1f, flightTime).SetEase(Ease.InOutSine));
        }

        /// <summary>近身斩击用：物理 slash、魔法 magic_bolt（可被 profile.ProjectileKey 覆盖）。</summary>
        static string StrikeKeyOf(PerformanceProfile profile, List<DamageEvent> damages)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileKey)) return profile.ProjectileKey;
            bool magic = damages.Count > 0 && damages[0].DamageType == "magic";
            return magic ? "magic_bolt" : "slash";
        }

        /// <summary>飞行弹道：物理 blade_bolt、魔法 magic_bolt（可被 profile.ProjectileKey 覆盖）。</summary>
        static string ProjectileKeyOf(PerformanceProfile profile, List<DamageEvent> damages)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileKey)) return profile.ProjectileKey;
            bool magic = damages.Count > 0 && damages[0].DamageType == "magic";
            return magic ? "magic_bolt" : "blade_bolt";
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
                case StatusTickEvent tick:
                    // 状态触发演出主体 = 状态持有者（反弹/反打/落雷宿主），
                    // 不用 source_id（神谕施法者，如雅典娜）——否则圣盾会演成雅典娜突进
                    return tick.Status?.OwnerId;
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
