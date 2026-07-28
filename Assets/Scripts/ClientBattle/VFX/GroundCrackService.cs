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
    //   本服务      触发条件 / 落点 / 朝向 / 分段（节拍由 StrikeSync 的飞行进度给）
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
        static bool IsPhysical(DamageEvent damage) =>
            damage != null && damage.DamageType != "magic";

        /// <summary>本组是否**有任一条**物理伤害该出裂地：总开关 + 物理 + 近 3D 地面。
        ///
        /// 只判「有没有」，具体哪一路出裂地由 <see cref="FlightPathCracks"/> **逐 lane**
        /// 判（2026-07-27 改）。曾只看 `damages[0]`，混合伤害组会整组跟着第一条走——
        /// 纯魔法那一路也拖出裂缝，或物理那一路反而没有。</summary>
        public static bool Active(List<DamageEvent> damages)
        {
            if (!Enabled || !Units.ArenaSlotLayout.GroundActive || damages == null) return false;
            foreach (var d in damages)
                if (IsPhysical(d)) return true;
            return false;
        }

        /// <summary>单条伤害是否该出命中裂地。
        /// 默认与 <see cref="Active"/> 同判据（魔法不裂）；特例：profile 显式配了
        /// <c>GroundStrengthTier ≥ 1</c> 时魔法也出命中裂地（神罚等，见
        /// ground_crack_config）。由 SettleDamage 与 HitKey 同帧调用，模板勿再单独 PlayHit。</summary>
        public static bool ShouldPlayHit(DamageEvent damage, PerformanceProfile profile = null) =>
            Enabled && damage != null && Units.ArenaSlotLayout.GroundActive
            && (IsPhysical(damage)
                || (profile != null && profile.GroundStrengthTier >= 1));

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
        /// 默认直径＝卡宽 ×1.5，可由 profile / 势能加强 / **巨伤**倍率放大。
        /// <paramref name="massive"/>＝重创横幅同判据 → 强制档 3 + 面积 ×1.5
        /// （与势能加强同规格，见 ground_crack_config.md）。</summary>
        public static void PlayHit(VFXContext ctx, PerformanceProfile profile,
                                   Units.UnitView target, bool massive = false)
        {
            if (target == null) return;
            var mode = GroundCrackPalette.ImpactMode;
            Play(ctx, mode, KeyOf(profile?.GroundHitKey, mode.Key),
                 Units.ArenaSlotLayout.GroundFoot(target.RestPosition), yaw: null,
                 ResolveStrength(profile, ctx, massive), AreaOf(profile, ctx, massive));
        }

        /// <summary>T1 弹道裂地驱动器：挂到 <see cref="StrikeSync"/> 的飞行段上，
        /// 沿途起裂并**按弹道飞行进度推进生长**——第 i 段覆盖进度区间
        /// [(i-1)/N, i/N]，在该区间内推满；末段推满那一刻＝弹道抵达＝命中拍开始。
        ///
        /// 禁止回到「按墙钟等分戳缝 + 贴花各自自走生长」：那样末段总在弹道抵达
        /// 之后才长完，命中拍被拖开半拍（P-57 / P-62）。
        ///
        /// 不该出裂地时返回 null（<see cref="StrikeSync.Attach"/> 对 null 免疫）。
        /// lane 序 ＝ damages 序 ＝ StrikeSync 的 projectiles/aims 序。</summary>
        public static IFlightDriven PathDriver(VFXContext ctx, PerformanceProfile profile,
                                               Vector3 from, List<DamageEvent> damages)
        {
            if (ctx == null || !Active(damages)) return null;
            return new FlightPathCracks(ctx, profile, from, damages);
        }

        /// <summary>T4 移动轨迹裂地：**出击者自己踩出来的一条 3 档裂缝**。
        /// 只在「拉满出手」时给——势能全开（<c>EmpoweredStrike</c>）或巨伤
        /// （<c>MassiveStrike</c>，重创横幅同判据）。语义是「这一步踏得地面开裂」，
        /// 与弹道裂地同骨架（PathMode）但起点是脚下、终点是他冲向的地方。
        ///
        /// 例：赫克托尔准备技能满势能又打出巨伤 → 突进到场心一路大裂缝，
        /// 弹道再从场心拉满到目标，命中处再炸一发档 3 ×1.5。
        ///
        /// 不该出时返回 null（调用方对 null 免疫）。物理判据与
        /// <see cref="Active"/> 同源：魔法伤害不裂地。</summary>
        public static MoveTrail MoveTrailDriver(VFXContext ctx, Units.UnitView mover,
                                                Vector3 destination, List<DamageEvent> damages)
        {
            if (ctx == null || mover == null || mover.Defeated) return null;
            if (!ctx.EmpoweredStrike && !ctx.MassiveStrike) return null;
            if (!Active(damages)) return null;
            return new MoveTrail(ctx, mover.transform.position, destination);
        }

        /// <summary>一条正在被踩出来的移动裂缝：按**实际位移进度**分段起裂并生长
        /// （不按墙钟——突进是 InQuint 加速，等分时间会让裂缝与脚步脱节）。</summary>
        public sealed class MoveTrail
        {
            readonly VFXContext _ctx;
            readonly Vector3 _fromFoot, _toFoot;
            readonly string[] _stepKeys;
            readonly GroundCrackPalette.Strength _strength;
            readonly Stamp[] _stamps;

            internal MoveTrail(VFXContext ctx, Vector3 from, Vector3 to)
            {
                _ctx = ctx;
                _stepKeys = GroundCrackPalette.PickPathKeys(null, PathSteps);
                _strength = ResolveStrength(null, ctx); // 门槛已保证＝档 3
                _fromFoot = Units.ArenaSlotLayout.GroundFoot(from);
                _toFoot = Units.ArenaSlotLayout.GroundFoot(to);
                _stamps = new Stamp[PathSteps];
            }

            /// <summary>按位移进度推进（每帧调；progress01 单调不回退）。</summary>
            public void Drive(float progress01)
            {
                for (int step = 0; step < PathSteps; step++)
                {
                    float start = step / (float)PathSteps;
                    if (progress01 < start) break;
                    _stamps[step] ??= Spawn(step);
                    _stamps[step].Drive(Mathf.InverseLerp(start, (step + 1) / (float)PathSteps,
                                                          progress01));
                }
            }

            /// <summary>抵近那一刻补齐并收满：禁止留半截裂痕（同 P-57/P-62 教训）。</summary>
            public void Finish() => Drive(1f);

            Stamp Spawn(int step)
            {
                Vector3 at = Vector3.Lerp(_fromFoot, _toFoot, (step + 1) / (float)PathSteps);
                var instance = Play(_ctx, GroundCrackPalette.PathMode,
                                    _stepKeys[Mathf.Clamp(step, 0, _stepKeys.Length - 1)],
                                    at, YawAlong(_fromFoot, _toFoot), _strength, area: 1f);
                return new Stamp(instance != null
                    ? instance.GetComponentsInChildren<GroundCrackDecal>(true)
                    : System.Array.Empty<GroundCrackDecal>());
            }
        }

        /// <summary>一段已出场的弹道裂地：出场时缓存贴花，供逐帧驱动生长
        /// （禁止每帧 GetComponentsInChildren —— Update 内零 alloc 红线）。</summary>
        sealed class Stamp
        {
            readonly GroundCrackDecal[] _decals;

            public Stamp(GroundCrackDecal[] decals)
            {
                _decals = decals;
                for (int i = 0; i < _decals.Length; i++)
                    if (_decals[i] != null) _decals[i].EnableFlightDriven();
            }

            public void Drive(float growth01)
            {
                for (int i = 0; i < _decals.Length; i++)
                    if (_decals[i] != null) _decals[i].DriveGrowth(growth01);
            }
        }

        sealed class FlightPathCracks : IFlightDriven
        {
            readonly VFXContext _ctx;
            readonly Vector3 _fromFoot;
            readonly Vector3[] _toFoot;
            readonly bool[] _hasTarget;
            readonly string[] _stepKeys;
            readonly GroundCrackPalette.Strength _strength;
            readonly Stamp[] _stamps; // [lane * PathSteps + step]，null＝该段还没起裂

            public FlightPathCracks(VFXContext ctx, PerformanceProfile profile,
                                    Vector3 from, List<DamageEvent> damages)
            {
                _ctx = ctx;
                // 专配优先；否则每段各抽不同变体 —— 同一 key 连戳三段＝「一道缝复读」
                _stepKeys = GroundCrackPalette.PickPathKeys(profile?.GroundPathKey, PathSteps);
                _strength = ResolveStrength(profile, ctx);
                _fromFoot = Units.ArenaSlotLayout.GroundFoot(from);

                int lanes = damages.Count;
                _toFoot = new Vector3[lanes];
                _hasTarget = new bool[lanes];
                _stamps = new Stamp[lanes * PathSteps];
                for (int lane = 0; lane < lanes; lane++)
                {
                    // 逐 lane 判物理：同组里魔法那一路不出裂地（纯魔法弹道无裂地）
                    if (!IsPhysical(damages[lane])) continue;
                    // 终点用原站位点，不跟 RestPosition 微抖；与定位圆同源
                    var unit = ctx.Unit(damages[lane].TargetId);
                    if (unit == null) continue;
                    _hasTarget[lane] = true;
                    _toFoot[lane] = Units.ArenaSlotLayout.GroundFoot(unit.HomePosition);
                }
            }

            public void OnFlightProgress(int lane, float progress01)
            {
                if (lane < 0 || lane >= _hasTarget.Length || !_hasTarget[lane]) return;
                for (int step = 0; step < PathSteps; step++)
                {
                    float start = step / (float)PathSteps;
                    // 起飞错峰期间弹道还停在出膛点：不许起裂，否则在施法者脚下堆一坨
                    if (progress01 < Mathf.Max(start, StrikeSync.LaunchedProgress)) break;
                    int slot = lane * PathSteps + step;
                    _stamps[slot] ??= Spawn(lane, step);
                    _stamps[slot].Drive(Mathf.InverseLerp(start, (step + 1) / (float)PathSteps,
                                                          progress01));
                }
            }

            public void OnFlightArrived()
            {
                // 兜底：弹道缺失/被提前回收时补齐整条路径并收满，禁止半截裂痕
                for (int lane = 0; lane < _hasTarget.Length; lane++)
                {
                    if (!_hasTarget[lane]) continue;
                    for (int step = 0; step < PathSteps; step++)
                    {
                        int slot = lane * PathSteps + step;
                        _stamps[slot] ??= Spawn(lane, step);
                        _stamps[slot].Drive(1f);
                    }
                }
            }

            /// <summary>第 step 段落在其区间末（＝弹道走到的位置），朝向锁定弹道方向。
            /// 弹道类不吃面积倍率：长度由弹道两端拉出来，放大只会溢出赛道。</summary>
            Stamp Spawn(int lane, int step)
            {
                var toFoot = _toFoot[lane];
                Vector3 at = Vector3.Lerp(_fromFoot, toFoot, (step + 1) / (float)PathSteps);
                var instance = Play(_ctx, GroundCrackPalette.PathMode,
                                    _stepKeys[Mathf.Clamp(step, 0, _stepKeys.Length - 1)],
                                    at, YawAlong(_fromFoot, toFoot), _strength, area: 1f);
                return new Stamp(instance != null
                    ? instance.GetComponentsInChildren<GroundCrackDecal>(true)
                    : System.Array.Empty<GroundCrackDecal>());
            }
        }

        // ------------------------------------------------------------ 内部

        /// <summary>专配 key 优先，否则用档位默认。</summary>
        static string KeyOf(string configured, string fallback) =>
            string.IsNullOrEmpty(configured) ? fallback : configured;

        /// <summary>强度档解析（权威规则见 docs/client/ground_crack_config.md）：
        /// 1. 巨伤（重创横幅同判据，<c>ctx.MassiveStrike</c>）或势能加强出手
        ///    → 强制档 3；两者都是「这一组整段拉满」，弹道/轨迹/命中同档；
        /// 2. 否则技能专配 <c>GroundStrengthTier</c>（0＝未配 → 档 1）；
        /// 3. 专配只升不降 —— 低于档 1 仍按档 1。
        ///
        /// 配置约定：准备型物理主动群攻配 2，瞬发物理主动群攻留 0（＝1）。</summary>
        static GroundCrackPalette.Strength ResolveStrength(PerformanceProfile profile,
                                                           VFXContext ctx,
                                                           bool massive = false)
        {
            if (massive || (ctx != null && (ctx.EmpoweredStrike || ctx.MassiveStrike)))
                return GroundCrackPalette.Strength.Blaze;
            const GroundCrackPalette.Strength baseline = GroundCrackPalette.Strength.Light;
            int configured = profile != null ? profile.GroundStrengthTier : 0;
            if (configured <= (int)baseline) return baseline;
            return (GroundCrackPalette.Strength)
                Mathf.Min(configured, (int)GroundCrackPalette.Strength.Blaze);
        }

        /// <summary>命中类面积倍率：巨伤或势能加强强制 1.5；否则未配取 1
        /// （＝卡宽 ×1.5 的默认大小）。</summary>
        static float AreaOf(PerformanceProfile profile, VFXContext ctx, bool massive = false)
        {
            if (massive || (ctx != null && (ctx.EmpoweredStrike || ctx.MassiveStrike)))
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
        static GameObject Play(VFXContext ctx, GroundCrackPalette.ModeSpec mode, string key,
                               Vector3 groundPos, float? yaw,
                               GroundCrackPalette.Strength strength, float area)
        {
            if (!Enabled || ctx?.Vfx == null) return null;
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
            return instance;
        }

        /// <summary>地面平面内 from→to 的偏航角（度）。遮罩长轴在 prefab 里是
        /// 局部 x，绕 y 旋转即让长轴对上弹道地面投影方向。</summary>
        static float YawAlong(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 1e-8f) return 0f;
            return -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        }
    }
}
