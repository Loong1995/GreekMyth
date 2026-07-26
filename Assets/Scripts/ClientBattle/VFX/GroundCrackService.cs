using System.Collections;
using System.Collections.Generic;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【裂地系统唯一入口】三个场景（弹道 / 命中 / 全局）的触发与几何。
    //
    // 每个场景 = **模式**（形状骨架：弹道类/命中类）+ **强度档**（缝宽/持续/亮度）
    // + **面积**（只命中类）。全局大裂地不是第三个骨架，而是命中类配大面积＋档 3。
    //
    // 演出模板（DefaultPerformance 等）只声明「有弹道飞了」「命中了谁」，
    // 由本服务决定是否播、播哪档、朝向与落点。演出代码里不得再出现
    // GroundCrackPalette / PlayAt("ground_crack_*") 之类直调 —— 加档、换 key、
    // 关停整套都只改本文件。
    //
    // 分层职责：
    //   本服务      触发条件 / 落点 / 朝向 / 分段节拍
    //   GroundCrackPalette  颜色 / 强度三档 / 模式两类（唯一真源）
    //   GroundCrackDecal    单实例的生长/熔岩/淡出
    //   GroundCrackComposer 编辑期烘 prefab（G4）
    //
    // 文档：docs/client/ground_crack_language.md
    // =========================================================================

    public static class GroundCrackService
    {
        /// <summary>整套裂地总开关（性能档位/调试用；关掉后所有档都不播）。</summary>
        public static bool Enabled = true;

        /// <summary>true 时每次触发打一条日志（排查「到底有没有接线」用）。</summary>
        public static bool LogTriggers;

        /// <summary>弹道飞行途中的分段数。</summary>
        const int PathSteps = 3;

        /// <summary>魔法伤害不裂地（裂地是「砸在地上」的语言）。</summary>
        static bool IsPhysical(List<DamageEvent> damages) =>
            damages != null && damages.Count > 0 && damages[0].DamageType != "magic";

        /// <summary>本组伤害是否该出裂地：总开关 + 物理 + 近 3D 地面存在。</summary>
        public static bool Active(List<DamageEvent> damages) =>
            Enabled && IsPhysical(damages) && Units.ArenaSlotLayout.GroundActive;

        /// <summary>势能全开加强出手时，命中类面积倍率（在卡宽×1.5 上再乘）。</summary>
        const float EmpoweredHitArea = 1.5f;

        /// <summary>场心大裂地的面积倍率：命中类骨架 ×这个数就是「全场被劈开」。
        /// 它不是第三个模式 —— 骨架与命中裂地同一件，只是更大更猛（2026-07-26 重组）。
        /// 仅叠加在势能加强出手上（与 Path/Hit 的档 3+1.5 并存）。</summary>
        const float ArenaArea = 3.2f;

        // ------------------------------------------------------------ 对外三场景
        //
        // 场景 = 模式（形状骨架）+ 强度（缝宽/持续/亮度）+ 面积（只命中类）。

        /// <summary>全局大裂地：势能全开的加强出手，场心起裂。
        /// ＝命中类骨架 + 档 3 + 大面积，恒定不受技能专配影响。
        /// Path/Hit 另由 <see cref="ResolveStrength"/> / <see cref="AreaOf"/>
        /// 升到档 3 + 面积 1.5（见 ground_crack_config.md）。</summary>
        public static void PlayArena(VFXContext ctx)
        {
            var mode = GroundCrackPalette.ImpactMode;
            Play(ctx, mode, mode.Key, Units.ArenaSlotLayout.GroundCenter(), yaw: null,
                 GroundCrackPalette.Strength.Blaze, ArenaArea);
        }

        /// <summary>命中裂地：受击者「卡在地板上的中心点」起放射圆裂纹。
        /// 默认直径＝卡宽 ×1.5，可由 profile / 势能加强倍率放大。</summary>
        public static void PlayHit(VFXContext ctx, PerformanceProfile profile, Units.UnitView target)
        {
            if (target == null) return;
            var mode = GroundCrackPalette.ImpactMode;
            Play(ctx, mode, KeyOf(profile?.GroundHitKey, mode.Key),
                 Units.ArenaSlotLayout.GroundFoot(target.RestPosition), yaw: null,
                 ResolveStrength(profile, ctx), AreaOf(profile, ctx));
        }

        /// <summary>T1 弹道裂地：飞行途中分 PathSteps 段，沿「施法者接地中心 →
        /// 受击者接地中心」连线起裂，朝向锁定弹道方向，终点即 T2 圆心。
        ///
        /// 进度取弹道实例的实时位置而非等分插值：弹道走带缓动的二次贝塞尔弧线、
        /// 且各道有起飞错峰，等分插值与眼睛看到的球对不上。
        /// 落点不用弹道正下方：弹道飞向竖立卡牌的卡心，其正下方比接地中心深
        /// halfCardH·sin(俯角)，直接投影会让裂痕带停在 T2 圆心后方、断成两截。
        ///
        /// 本协程**同时承担飞行期的等待**（总时长 = flight），调用方不要再另垫。</summary>
        public static IEnumerator PlayPath(VFXContext ctx, PerformanceProfile profile,
                                           Vector3 from, List<DamageEvent> damages,
                                           Transform[] projectiles, float flight)
        {
            var mode = GroundCrackPalette.PathMode;
            // 专配优先；否则每段各抽不同变体 —— 同一 key 连戳三段＝「两道大缝复读到终点」
            var stepKeys = GroundCrackPalette.PickPathKeys(profile?.GroundPathKey, PathSteps);
            var strength = ResolveStrength(profile, ctx);
            // 出膛点按卡心投影（弹道实例就生在卡心，用于量飞行进度）；
            // 裂地线段两端按接地中心，与命中裂地 T2 的圆心同源
            var launchGround = Units.ArenaSlotLayout.GroundUnder(from);
            var fromFoot = Units.ArenaSlotLayout.GroundFoot(from);
            float elapsed = 0f;
            for (int s = 1; s <= PathSteps; s++)
            {
                float when = flight * s / (PathSteps + 1f);
                if (when > elapsed)
                {
                    yield return new WaitForSeconds(when - elapsed);
                    elapsed = when;
                }
                string key = stepKeys[s - 1];
                for (int i = 0; i < damages.Count; i++)
                {
                    var projectile = i < projectiles.Length ? projectiles[i] : null;
                    var target = ctx.UnitTransform(damages[i].TargetId);
                    if (projectile == null || target == null) continue;
                    var aimGround = Units.ArenaSlotLayout.GroundUnder(target.position);
                    var atGround = Units.ArenaSlotLayout.GroundUnder(projectile.position);
                    float progress = Progress(launchGround, aimGround, atGround);
                    // 错峰未起飞的弹道仍停在施法点，此时起裂会在施法者脚下堆一坨
                    if (progress < 0.05f) continue;
                    var toFoot = Units.ArenaSlotLayout.GroundFoot(target.position);
                    // 弹道类不吃面积倍率：它的长度由弹道两端拉出来，放大只会溢出赛道
                    Play(ctx, mode, key, Vector3.Lerp(fromFoot, toFoot, progress),
                         YawAlong(fromFoot, toFoot), strength, area: 1f);
                }
            }
            if (flight > elapsed) yield return new WaitForSeconds(flight - elapsed);
        }

        // ------------------------------------------------------------ 内部

        /// <summary>专配 key 优先，否则用档位默认。</summary>
        static string KeyOf(string configured, string fallback) =>
            string.IsNullOrEmpty(configured) ? fallback : configured;

        /// <summary>强度档解析（权威规则见 docs/client/ground_crack_config.md）：
        /// 1. 势能加强出手（`ctx.EmpoweredStrike`）→ 强制档 3；
        /// 2. 否则技能专配 `GroundStrengthTier`（0＝未配 → 档 1）；
        /// 3. 专配只升不降 —— 低于档 1 仍按档 1。
        ///
        /// 配置约定：准备型物理主动群攻配 2，瞬发物理主动群攻留 0（＝1）。</summary>
        static GroundCrackPalette.Strength ResolveStrength(PerformanceProfile profile,
                                                           VFXContext ctx)
        {
            if (ctx != null && ctx.EmpoweredStrike)
                return GroundCrackPalette.Strength.Blaze;
            const GroundCrackPalette.Strength baseline = GroundCrackPalette.Strength.Light;
            int configured = profile != null ? profile.GroundStrengthTier : 0;
            if (configured <= (int)baseline) return baseline;
            return (GroundCrackPalette.Strength)
                Mathf.Min(configured, (int)GroundCrackPalette.Strength.Blaze);
        }

        /// <summary>命中类面积倍率：势能加强出手强制 1.5；否则未配取 1
        /// （＝卡宽 ×1.5 的默认大小）。</summary>
        static float AreaOf(PerformanceProfile profile, VFXContext ctx)
        {
            if (ctx != null && ctx.EmpoweredStrike)
                return EmpoweredHitArea;
            float area = profile != null ? profile.GroundHitArea : 0f;
            return area > 0.01f ? area : 1f;
        }

        /// <summary>播一个裂地实例。yaw 非空则锁定朝向（弹道类），否则由
        /// GroundCrackDecal 随机绕地面法线转（命中类）。
        ///
        /// 存续**故意不吃倍速**（只吃 DurationMul 放慢倍率）：裂地是留在地上的
        /// 痕迹、不 yield 不阻塞节拍，4 倍速下若同比压缩，整段只剩 ~0.4s，
        /// 肉眼等于没播（2026-07-26 赫克托尔群攻实测）。快进时痕迹以常速淡出。</summary>
        static void Play(VFXContext ctx, GroundCrackPalette.ModeSpec mode, string key,
                         Vector3 groundPos, float? yaw,
                         GroundCrackPalette.Strength strength, float area)
        {
            if (!Enabled || ctx?.Vfx == null) return;
            var spec = GroundCrackPalette.SpecOf(strength, mode.Kind);
            float pace = Mathf.Max(0.5f, ctx.DurationMul);
            float life = spec.Duration * pace;
            var instance = ctx.Vfx.PlayAt(key, groundPos, life);
            // 池化复用：根旋转必须每次重设，否则上一发的 yaw 会留在实例上
            instance.transform.rotation = yaw.HasValue
                ? Quaternion.Euler(0f, yaw.Value, 0f)
                : Quaternion.identity;
            foreach (var decal in instance.GetComponentsInChildren<GroundCrackDecal>(true))
            {
                // 贴花动画与实例存活时长同一把尺，否则会被提前回收截断
                decal.DurationScale = pace;
                // 骨架来自 prefab，烈度与大小出场现写：同一件三档通吃、任意面积通吃，
                // 不为分档或分大小裂 prefab 变体
                decal.ApplyStrength(strength, mode.Kind);
                decal.ApplyArea(area);
            }
            if (LogTriggers)
                Debug.Log($"[GroundCrack] {key} @{groundPos} yaw={(yaw.HasValue ? yaw.Value : 0f)} " +
                          $"strength={strength} area={area:F2} life={life:F2}s");
        }

        /// <summary>地面平面内 from→to 的偏航角（度）。遮罩长轴在 prefab 里是
        /// 局部 x，绕 y 旋转即让长轴对上弹道地面投影方向。</summary>
        static float YawAlong(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 1e-8f) return 0f;
            return -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        }

        /// <summary>弹道当前地面投影点在「出膛点→瞄准点」上走过的比例（0~1）。
        /// 取投影分量而非直线距离：弹道弧线的水平分量才代表推进程度。</summary>
        static float Progress(Vector3 launch, Vector3 aim, Vector3 at)
        {
            Vector3 axis = aim - launch;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return 1f;
            return Mathf.Clamp01(Vector3.Dot(at - launch, axis) / len2);
        }
    }
}
