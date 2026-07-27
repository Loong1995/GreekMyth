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
            var heals = group.All<HealEvent>();
            string actorId = ActorOf(group);
            var actor = ctx.Unit(actorId);
            string floatName = FloatNameOf(group);

            // 台词由 TraitLineExtractProcessor 抽成独占 TraitLine 组，此处不播

            // 协击标（B1）：队友普攻后的追加协击，出手前挂「协击」角标
            if (group.Root is NormalAttackEvent { Kind: "coordinated" } && actor != null)
                ctx.Floats.Show(actor, "协击", new Color(0.5f, 0.85f, 1f), 1.1f);

            // 无伤害/治疗：prepare/纯状态主动等——只飘技能名+落账，禁止走 AoeCenter/Melee
            // （否则战吼 prepare 会被专配 AoeCenter 空跑进中心，观感像「没放技能」）
            if (damages.Count == 0 && heals.Count == 0)
            {
                if (group.Root is SkillTriggerEvent && actor != null)
                    ctx.Floats.ShowSkillName(actor, floatName);
                ctx.Sfx.Play(SfxOf(group, profile));
                foreach (var ev in group.Events)
                    if (ev is not TraitTriggerEvent)
                        SettleSideEvent(ev, ctx);
                yield break;
            }

            // 纯治疗组（无伤害）：禁止走 Melee/CastKey（圣盾反弹闪光），否则无治疗飘字且误闪反击盾
            // 圣盾重击回血：另闪 icon_aegis_heal（与反伤 icon_aegis 区分）
            if (damages.Count == 0 && heals.Count > 0)
            {
                bool aegisHeal = IsAegisShieldTick(group, profile);
                foreach (var heal in heals)
                {
                    if (aegisHeal)
                        ctx.Unit(heal.TargetId)?.FlashOverlayIcon("icon_aegis_heal",
                            tint: new Color(0.45f, 0.92f, 0.65f), duration: ctx.Scaled(0.75f));
                    SettleHeal(heal, ctx, floatName);
                    yield return new WaitForSeconds(ctx.Scaled(0.3f));
                }
                foreach (var ev in group.Events)
                    if (ev is not HealEvent && ev is not TraitTriggerEvent)
                        SettleSideEvent(ev, ctx);
                yield break;
            }

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
            // 仅专配 CastKey（如圣盾）在 Melee 突进前播；主动默认不再播 Cast
            if (!string.IsNullOrEmpty(profile.CastKey) && actor != null
                && template == PerformanceTemplate.Melee)
            {
                ctx.Vfx.PlayAt(profile.CastKey, actor.transform.position, ctx.Scaled(0.55f));
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

            // 伤害模板内未处理的治疗：统一 SettleHeal（飘字+特效+兵力）
            foreach (var heal in heals)
            {
                SettleHeal(heal, ctx, floatName);
                yield return new WaitForSeconds(ctx.Scaled(0.3f));
            }

            // 剩余副事件（状态/属性/兵力/阵亡等）统一收尾表现；治疗已结算
            foreach (var ev in group.Events)
                if (ev is not DamageEvent && ev is not HealEvent && ev is not TraitTriggerEvent)
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

            // 特殊图标（阿喀琉斯裂甲：仅傲慢贯穿 25% 成功；渐变闪入闪出）
            if (!string.IsNullOrEmpty(profile.ExtraIconKey) && damages.Count > 0
                && (!profile.ExtraIconRequiresPierceBoost || group.PierceBoost))
            {
                var lastTarget = ctx.Unit(damages[damages.Count - 1].TargetId);
                if (lastTarget != null)
                    PlayFadingExtraIcon(ctx, profile, lastTarget.transform);
            }
        }

        /// <summary>ExtraIcon 渐变闪：淡入 → 短持 → 淡出（不占节拍）。</summary>
        static void PlayFadingExtraIcon(VFXContext ctx, PerformanceProfile profile, Transform host)
        {
            float life = ctx.Scaled(0.85f);
            var icon = ctx.Vfx.PlayOn(profile.ExtraIconKey, host, life,
                new Vector3(0f, 0.3f, -0.5f));
            icon.transform.localScale *= profile.ExtraIconScale;
            var sr = icon.GetComponent<SpriteRenderer>()
                     ?? icon.GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return;
            Color c = sr.color;
            c.a = 0f;
            sr.color = c;
            var seq = DOTween.Sequence().SetLink(icon);
            seq.Append(DOTween.To(() => sr.color, x => sr.color = x,
                new Color(c.r, c.g, c.b, 1f), ctx.Scaled(0.12f)));
            seq.AppendInterval(ctx.Scaled(0.4f));
            seq.Append(DOTween.To(() => sr.color, x => sr.color = x,
                new Color(c.r, c.g, c.b, 0f), ctx.Scaled(0.28f)));
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
                    Vector3 strikePos = Vector3.Lerp(target.RestPosition, striker.RestPosition, 0.28f);
                    var dash = striker.transform.DOMove(strikePos, ctx.Scaled(0.22f))
                        .SetEase(Ease.InQuad).SetLink(striker.gameObject);
                    yield return dash.WaitForCompletion();
                }
                if (target != null)
                {
                    var strike = ctx.Vfx.PlayAt(strikeKey,
                        target.RestPosition + new Vector3(0f, 0.2f, -0.5f), ctx.Scaled(0.45f));
                    strike.transform.localScale *= strikeScale;
                }
                // 命中裂地与 HitKey 同帧：统一在 SettleDamage，勿在此重复 PlayHit
                SettleDamage(damage, profile, ctx, floatName);
                if (striker != null && !striker.Defeated)
                {
                    var back = striker.DOMoveReturnHome(ctx.Scaled(0.24f));
                    yield return back.WaitForCompletion();
                }
            }
        }

        // ---------------------------------------------------------- 群攻主动

        IEnumerator PlayAoeCenter(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                                  Units.UnitView actor, List<DamageEvent> damages, string floatName)
        {
            // 施法者移动至卡盘中心 → 齐射弹道（主动默认不再播 Cast）
            if (actor != null && !actor.Defeated)
            {
                var move = actor.transform.DOMove(ctx.BoardCenter, ctx.Scaled(0.3f))
                    .SetEase(Ease.OutQuad).SetLink(actor.gameObject);
                yield return move.WaitForCompletion();
            }

            // N 道弹道错峰起飞、同时段抵达 → 抵达帧同帧结算掉血
            string projectileKey = ProjectileKeyOf(profile, damages);
            Vector3 from = actor != null ? actor.transform.position : ctx.BoardCenter;
            float flightBase = ctx.Scaled(0.38f);
            float stagger = damages.Count > 1 ? ctx.Scaled(0.045f) : 0f;
            var projectiles = new Transform[damages.Count]; // lane 序＝damages 序
            var aims = new Vector3[damages.Count];
            for (int i = 0; i < damages.Count; i++)
            {
                var target = ctx.UnitTransform(damages[i].TargetId);
                if (target == null) continue;
                float delay = i * stagger;
                aims[i] = target.position;
                var launched = LaunchProjectile(ctx, projectileKey, from, aims[i],
                    flightBase - delay, delay);
                projectiles[i] = launched != null ? launched.transform : null;
            }
            // T3 全局大裂地：势能全开的加强出手，逻辑圆量级主缝从场心劈开。
            // 与弹道同帧起，靠 T1/T2 在其上叠加，读作「一击震裂全场」
            if (ctx.EmpoweredStrike && GroundCrackService.Active(damages))
                GroundCrackService.PlayArena(ctx);
            // 飞行段：弹道裂地跟着弹道长；协程返回＝弹道抵达＝路径生长收满
            yield return StrikeSync.Fly(from, projectiles, aims, flightBase)
                .Attach(GroundCrackService.PathDriver(ctx, profile, from, damages))
                .Run();
            // 命中拍：与抵达同帧（命中裂地+命中特效+受击抖动，见 SettleDamage）
            foreach (var damage in damages)
                SettleDamage(damage, profile, ctx, floatName);
            // 命中特效/受击闪烁与回身位移同播，命中后不垫定格
            if (actor != null && !actor.Defeated)
            {
                var back = actor.DOMoveReturnHome(ctx.Scaled(0.3f));
                yield return back.WaitForCompletion();
            }
        }

        // ---------------------------------------------------------- 远程落击（雷霆）

        IEnumerator PlayRemoteStrike(EventGroup group, PerformanceProfile profile, VFXContext ctx,
                                     List<DamageEvent> damages, string floatName)
        {
            // 宙斯雷霆等：目标头顶头像标 + 自上而下贯穿（无 ProjectileKey → DR 程序化雷；
            // 有 key 时走 VFX 弹道，供其它 RemoteStrike 复用）
            float strikeTime = ctx.Scaled(0.35f);
            foreach (var damage in damages)
            {
                var target = ctx.Unit(damage.TargetId);
                if (target == null) continue;
                if (!string.IsNullOrEmpty(profile.PortraitMarkKey))
                    target.ShowPortraitMark(profile.PortraitMarkKey, duration: ctx.Scaled(1.6f));
            }
            yield return new WaitForSeconds(ctx.Scaled(0.06f));
            bool useVfxBolt = !string.IsNullOrEmpty(profile.ProjectileKey);
            foreach (var damage in damages)
            {
                var target = ctx.Unit(damage.TargetId);
                if (target == null) continue;
                var center = target.RestPosition;
                // FrameSlotH≈2.3：从卡顶上方劈穿到卡底下方
                var from = center + new Vector3(Random.Range(-0.08f, 0.08f), 1.35f, -0.55f);
                var to = center + new Vector3(Random.Range(-0.12f, 0.12f), -1.35f, -0.55f);
                if (useVfxBolt)
                    LaunchProjectile(ctx, profile.ProjectileKey, from, to, strikeTime);
                else
                {
                    var bolt = DrLightningUtil.Spawn(ctx.Vfx.transform, "StrikeBolt");
                    DrLightningUtil.Fire(bolt, from, to, duration: strikeTime, chaos: 0.22f,
                                         generations: 6, alpha: 0.2f, widthMul: 0.7f, sortingOrder: 50);
                    Object.Destroy(bolt.gameObject, strikeTime + 0.05f);
                }
                // HitKey / 抖动 / 裂地一律等落劈结束进 SettleDamage，禁止提前炸点
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
            // 单体弹道同样出裂地（与群攻同一套服务与同步器）：一段一条，朝向沿本段弹道
            var single = new List<DamageEvent>(1) { null };
            var projectile = new Transform[1];
            var aim = new Vector3[1];
            foreach (var damage in damages)
            {
                var target = ctx.UnitTransform(damage.TargetId);
                if (target != null)
                {
                    float flight = ctx.Scaled(0.30f);
                    Vector3 from = actor != null ? actor.transform.position : ctx.BoardCenter;
                    aim[0] = target.position;
                    var launched = LaunchProjectile(ctx, projectileKey, from, aim[0], flight);
                    single[0] = damage;
                    projectile[0] = launched != null ? launched.transform : null;
                    yield return StrikeSync.Fly(from, projectile, aim, flight)
                        .Attach(GroundCrackService.PathDriver(ctx, profile, from, single))
                        .Run();
                }
                SettleDamage(damage, profile, ctx, floatName); // 命中拍与抵达同帧
                // 段间不垫定格：下一段弹道立刻起飞，受击闪烁/飘字与其同播
            }
            // 治疗统一在 Play 收尾 SettleHeal（避免与 Melee 等模板漏结算）
        }

        // ---------------------------------------------------------- 工具

        /// <summary>弹道：朝向飞行、二次贝塞尔微弧、出生缩放；可选起飞错峰（群攻齐射）。
        /// 返回弹道实例，供地面裂地跟随其实时位置。</summary>
        static GameObject LaunchProjectile(VFXContext ctx, string key, Vector3 from, Vector3 to,
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
            return projectile;
        }

        /// <summary>近身斩击用：物理 slash、魔法 magic_bolt（可被 profile.ProjectileKey 覆盖）。</summary>
        static string StrikeKeyOf(PerformanceProfile profile, List<DamageEvent> damages)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileKey)) return profile.ProjectileKey;
            bool magic = damages.Count > 0 && damages[0].DamageType == "magic";
            return magic ? "magic_bolt" : "slash";
        }

        /// <summary>主动飞行弹道默认：物理 proj_bolt200、魔法 magic_bolt。</summary>
        static string ProjectileKeyOf(PerformanceProfile profile, List<DamageEvent> damages)
        {
            if (!string.IsNullOrEmpty(profile.ProjectileKey)) return profile.ProjectileKey;
            bool magic = damages.Count > 0 && damages[0].DamageType == "magic";
            return magic ? "magic_bolt" : "proj_bolt200";
        }

        static int CountDistinctTargets(List<DamageEvent> damages)
        {
            var ids = new HashSet<string>();
            foreach (var damage in damages) ids.Add(damage.TargetId);
            return ids.Count;
        }

        /// <summary>埃癸斯圣盾 status_tick（反弹或重击回血）。</summary>
        static bool IsAegisShieldTick(EventGroup group, PerformanceProfile profile)
        {
            if (profile != null && profile.SkillOrStatusId == "aegis_shield") return true;
            return group.Root is StatusTickEvent tick && tick.Status?.StatusId == "aegis_shield";
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
