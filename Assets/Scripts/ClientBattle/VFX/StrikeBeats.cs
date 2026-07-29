using System.Collections;
using ClientBattle.Units;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【出手三拍】预备 → 发力 → 收势，任何「移动过去打」的模板共用。
    //
    // 为什么是三拍：动作游戏里没有一次出手是单拍的。只有「冲过去」的话，
    // 观众的眼睛没有「要来了」的预期，命中落地也就没有兑现感 —— 读作瞬移，
    // 不读作出手。三拍分别承担：
    //   预备 Windup ：反向蓄一小口气（OutQuad 减速停住），给出「要打了」的预告；
    //   发力 Strike ：加速冲入（InQuint 末速最高），命中拍正好落在最快的一帧，
    //                 途中留残影表达速度（AfterImageService）；
    //   收势 Recover：过冲回弹（OutBack），读作「收招」而不是「倒带」。
    //
    // 时长全部经 ctx.Scaled，吃倍速与 DurationMul；三拍都是可见位移，
    // 不违背零死帧（R-1 静帧只允许单挑横幅）。
    //
    // 文档：docs/client/performance_mechanisms.md（出手三拍）
    // =========================================================================

    public static class StrikeBeats
    {
        /// <summary>预备 + 发力：先反向蓄力，再加速突进到 destination，途中留残影。
        /// 协程结束＝已抵达 destination，调用方随后开命中拍。
        ///
        /// <paramref name="damages"/> 传本组伤害时，**拉满出手**（势能全开或巨伤）
        /// 会在突进轨迹上踩出一条档 3 裂缝（T4，判据与档位全在
        /// <see cref="GroundCrackService.MoveTrailDriver"/>；本类不判裂地规则）。</summary>
        public static IEnumerator Advance(VFXContext ctx, UnitView mover, Vector3 destination,
                                          System.Collections.Generic.List<Events.DamageEvent>
                                              damages = null)
        {
            if (mover == null || mover.Defeated) yield break;
            var t = mover.transform;

            Vector3 forward = destination - t.position;
            float dist = forward.magnitude;
            if (dist > 1e-4f)
            {
                float back = Mathf.Min(StagePerformanceConfig.WindupRatio * dist,
                                       StagePerformanceConfig.WindupMax);
                yield return t.DOMove(t.position - forward / dist * back,
                        ctx.Scaled(StagePerformanceConfig.WindupSeconds))
                    .SetEase(Ease.OutQuad).SetLink(mover.gameObject).WaitForCompletion();
            }

            // 轨迹裂地起点＝蓄力后的实际站点（不是蓄力前），否则裂缝会从空处开始
            var trail = GroundCrackService.MoveTrailDriver(ctx, mover, destination, damages);
            float dur = ctx.Scaled(StagePerformanceConfig.StrikeSeconds);
            var dash = t.DOMove(destination, dur)
                .SetEase(Ease.InQuint).SetLink(mover.gameObject);
            yield return TrailWhile(ctx, mover, dur, trail, t.position, destination);
            yield return dash.WaitForCompletion();
            trail?.Finish(); // 抵近同帧收满，命中拍不被半截裂痕拖开
        }

        /// <summary>收势：过冲回弹到休息点。落点**沿本次行动方向前移**
        /// （`towardWorld`＝这一拍冲向的地方）——打出去的人往前站一点，
        /// 与受击者被推回去一点互为一对；两者都夹在微调圆内。</summary>
        public static IEnumerator Recover(VFXContext ctx, UnitView mover, Vector3 towardWorld)
        {
            if (mover == null || mover.Defeated) yield break;
            // 注意（2026-07-28 实测）：DOTween 的 WaitForCompletion 并不等满时长，
            // 这一拍实际只阻塞约 0.1s（离线模型据实测建模，见 P-84）。
            yield return mover.DOMoveReturnHomeToward(towardWorld,
                    ctx.Scaled(StagePerformanceConfig.RecoverSeconds), Ease.OutBack)
                .WaitForCompletion();
        }

        /// <summary>突进期间按固定间隔拍残影。自己走时钟而不是挂 tween 回调：
        /// 回调数量随帧率浮动，固定间隔才能保证低端机上也是一串而不是两张。
        ///
        /// 顺带每帧驱动轨迹裂地（如有）。进度取**实际位移占比**而非时间占比：
        /// 突进是 InQuint 加速，按时间等分会让裂缝跑在脚前面。</summary>
        static IEnumerator TrailWhile(VFXContext ctx, UnitView mover, float duration,
                                      GroundCrackService.MoveTrail trail,
                                      Vector3 from, Vector3 to)
        {
            float interval = Mathf.Max(0.01f, ctx.Scaled(StagePerformanceConfig.GhostInterval));
            float life = ctx.Scaled(StagePerformanceConfig.GhostLife);
            float span = (to - from).magnitude;
            float elapsed = 0f, since = interval; // 首帧即出一张，突进起点就有尾巴
            while (elapsed < duration)
            {
                if (since >= interval)
                {
                    since -= interval;
                    AfterImageService.Emit(mover, life);
                }
                if (trail != null && span > 1e-4f && mover != null)
                    trail.Drive(Mathf.Clamp01(
                        Vector3.Dot(mover.transform.position - from, (to - from) / span) / span));
                yield return null;
                float dt = Time.deltaTime;
                elapsed += dt;
                since += dt;
            }
        }
    }
}
