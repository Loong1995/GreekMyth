using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【出手同步器】一次「发射 → 飞行 → 命中」的时间轴唯一真源。
    //
    // 契约（所有带弹道的模板共用，禁止各自 WaitForSeconds 拼时序）：
    //   1. 飞行段：进度 0→1 取**弹道实例的真实位置**（含贝塞尔弧线、缓动、
    //      起飞错峰），逐帧广播给挂上来的表现（IFlightDriven）。
    //   2. 抵达帧：进度强制推满 1（挂载的生长同时收满）→ OnFlightArrived
    //      → Run() 返回。
    //   3. 调用方在 Run() 之后**同帧**开命中拍（SkillPerformance.SettleDamage：
    //      命中裂地 + 命中特效 + 受击抖动 + 震屏）。
    //
    // 低耦合：本类不认识裂地/音效/镜头，只发进度；表现方只认进度不认弹道实现。
    // 想让新表现跟弹道走 → 实现 IFlightDriven 并 Attach，无需改本类与模板。
    //
    // lane（轨道）序 ＝ 调用方传入的 projectiles/aims 序 ＝ damages 序，
    // 三者必须同序，驱动方按 lane 找自己的目标。
    //
    // 文档：docs/client/performance_mechanisms.md（出手同步）
    // =========================================================================

    /// <summary>挂在飞行段上的表现：只吃归一化进度，不认弹道实现。</summary>
    public interface IFlightDriven
    {
        /// <summary>某轨飞行进度推进（每帧调用；progress01 单调不回退）。</summary>
        void OnFlightProgress(int lane, float progress01);

        /// <summary>全部抵达（进度已推满 1）后调用一次；命中拍随后同帧开。</summary>
        void OnFlightArrived();
    }

    public sealed class StrikeSync
    {
        /// <summary>弹道还没真正动起来的进度阈值：起飞错峰期间弹道仍停在出膛点，
        /// 低于此值视作「未起飞」，驱动方据此避免在施法者脚下先堆一坨。</summary>
        public const float LaunchedProgress = 0.03f;

        readonly Vector3 _launchGround;
        readonly Transform[] _projectiles;
        readonly Vector3[] _aimGrounds;
        readonly float[] _progress;
        readonly List<IFlightDriven> _driven = new();
        readonly float _flightSeconds;

        StrikeSync(Vector3 from, Transform[] projectiles, Vector3[] aims, float flightSeconds)
        {
            _launchGround = Units.ArenaSlotLayout.GroundUnder(from);
            _projectiles = projectiles;
            _flightSeconds = flightSeconds;
            int lanes = aims != null ? aims.Length : 0;
            _aimGrounds = new Vector3[lanes];
            _progress = new float[lanes];
            for (int i = 0; i < lanes; i++)
                _aimGrounds[i] = Units.ArenaSlotLayout.GroundUnder(aims[i]);
        }

        /// <summary>开一段飞行。aims＝各轨瞄准点（与发射弹道时传的终点同一个），
        /// projectiles＝各轨弹道实例（可含 null，缺的那轨退回墙钟进度）。</summary>
        public static StrikeSync Fly(Vector3 from, Transform[] projectiles, Vector3[] aims,
                                     float flightSeconds) =>
            new StrikeSync(from, projectiles, aims, flightSeconds);

        /// <summary>挂一个跟飞行进度走的表现；null 免疫（该表现本次不参与时直接传 null）。</summary>
        public StrikeSync Attach(IFlightDriven driven)
        {
            if (driven != null) _driven.Add(driven);
            return this;
        }

        public float ProgressOf(int lane) =>
            lane >= 0 && lane < _progress.Length ? _progress[lane] : 0f;

        /// <summary>跑完飞行段：协程结束＝弹道抵达＝挂载生长已收满。
        /// 本协程**已承担飞行期等待**，调用方不要再另垫时间。</summary>
        public IEnumerator Run()
        {
            float flight = Mathf.Max(0.01f, _flightSeconds);
            float elapsed = 0f;
            while (elapsed < flight)
            {
                Tick(elapsed / flight);
                yield return null;
                elapsed += Time.deltaTime;
            }
            // 抵达帧：进度收满，挂载的生长在这一帧到位；随后调用方同帧开命中拍
            for (int lane = 0; lane < _progress.Length; lane++)
            {
                _progress[lane] = 1f;
                Broadcast(lane, 1f);
            }
            for (int i = 0; i < _driven.Count; i++)
                _driven[i].OnFlightArrived();
        }

        void Tick(float wallClock01)
        {
            for (int lane = 0; lane < _progress.Length; lane++)
            {
                // 单调不回退：弹道被回池/瞬移时进度不许倒着走，否则裂缝会缩回去
                _progress[lane] = Mathf.Max(_progress[lane], Sample(lane, wallClock01));
                Broadcast(lane, _progress[lane]);
            }
        }

        void Broadcast(int lane, float progress01)
        {
            for (int i = 0; i < _driven.Count; i++)
                _driven[i].OnFlightProgress(lane, progress01);
        }

        /// <summary>某轨当前进度：弹道地面投影点在「出膛点→瞄准点」轴上走过的比例。
        /// 取投影分量而非直线距离——弹道走弧线，只有水平分量代表推进程度。
        /// 无弹道实例（占位缺失/已回池）时退回墙钟，保证时间轴照样收口。</summary>
        float Sample(int lane, float wallClock01)
        {
            var projectile = _projectiles != null && lane < _projectiles.Length
                ? _projectiles[lane] : null;
            if (projectile == null) return wallClock01;
            Vector3 axis = _aimGrounds[lane] - _launchGround;
            float len2 = axis.sqrMagnitude;
            if (len2 < 1e-6f) return wallClock01;
            Vector3 at = Units.ArenaSlotLayout.GroundUnder(projectile.position);
            return Mathf.Clamp01(Vector3.Dot(at - _launchGround, axis) / len2);
        }
    }
}
