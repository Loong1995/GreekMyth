using System;
using System.Collections;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】CutInStage：**一切 cut-in 横幅的统一取景**。
    //
    // 定论（2026-07-27）：cut-in 只有一种编排形状，与单挑同构——
    //
    //     推镜 → cut-in 横幅 → 该组本身的演出（出手/命中）→ 撤镜
    //
    // 全程**独占播放单元**（调用方 yield 本协程，切完才出手、还位才交棒）。
    // 单挑是本形状的唯一特例：它在横幅那一拍额外做「立绘飞出/飞回」
    // （见 DuelStage）；其余 cut-in 不飞立绘，其它逻辑完全一致。
    //
    // 【为什么不把运镜写进 CutInService】CutInService 负责的是**屏幕构件**
    // （暗幕/斜带/立绘/标题，挂在相机上）；运镜是**世界侧**的事，唯一写方是
    // StageCameraRig。混在一起会让「谁接管相机、谁负责还」再次散开——单挑
    // 已经为此翻过车（HardStop 卡在近机位）。本类只做编排：借 rig、按拍走、
    // finally 归还。
    //
    // 【为什么高伤 cut-in 要预判而不是事后补】客户端在播一组之前就持有整组
    // 事件，能提前知道「这一下会打出巨额伤害」。事后回调式（旧
    // NotifyDamageSettled）只能在命中后才切横幅，既做不到「伤害前推镜」，
    // 还会让暗幕盖住刚起播的命中特效（P-72 的成因）。判定统一在编译期
    // CutInPlanner（Events 层），运行期只读 EventGroup.CutIn 注记。
    //
    // 文档：docs/client/cutin_stage.md（权威）、playback_requirements R-5.2
    // =========================================================================

    public static class CutInStage
    {
        /// <summary>取景式 cut-in：推镜 → 横幅 → <paramref name="body"/> → 撤镜。
        ///
        /// <paramref name="body"/> 是本组原本的演出协程（出手+命中）。放在推近的
        /// 机位上播，命中结束才撤镜——「运镜靠近、切横幅、打这一下、镜头回去」。
        /// 传 null 则只播横幅（无后续出手的场合，如追击计数横幅）。
        ///
        /// 相机归还走 finally：中断（HardStop/CancelAll）也不会把战斗留在近景。</summary>
        public static IEnumerator Play(VFXContext ctx, UnitView focus, string title,
                                      int groupId, Func<IEnumerator> body = null)
        {
            // 同组已切过：不推镜也不再切，直接把 body 按常规机位播完
            if (ctx?.CutIns == null || focus == null || ctx.CutIns.AlreadyPlayed(groupId))
            {
                if (body != null) yield return body();
                yield break;
            }

            var rig = StageCameraRig.Ensure();
            bool bodyDone = false;
            try
            {
                yield return PushIn(ctx, rig);
                yield return ctx.CutIns.PlaySoloBlocking(ctx, focus, title, groupId);
                if (body != null) yield return body();
                bodyDone = true;
                yield return PullOut(ctx, rig);
            }
            finally
            {
                // 正常路径已撤到位，Release 只是幂等收尾；异常/中断路径靠它还位。
                // bodyDone 只用于说明意图，Release 本身无条件调。
                _ = bodyDone;
                rig?.Release();
            }
        }

        /// <summary>推镜一拍：抬俯角 + 缩距离，到位后**定住**。
        /// 定住那一下才让人确认「镜头到位、卡面变大了」（与单挑同理）。</summary>
        static IEnumerator PushIn(VFXContext ctx, StageCameraRig rig)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.CutInCameraPushSeconds);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                Blend(rig, Mathf.Clamp01(t / dur));
                yield return null;
            }
            Blend(rig, 1f);
            float hold = StagePerformanceConfig.CutInCameraHoldSeconds;
            if (hold > 0f) yield return new WaitForSeconds(hold);
        }

        /// <summary>撤镜一拍：回到 CameraFitter 的常规机位。</summary>
        static IEnumerator PullOut(VFXContext ctx, StageCameraRig rig)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.CutInCameraPushSeconds);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                Blend(rig, 1f - Mathf.Clamp01(t / dur));
                yield return null;
            }
            Blend(rig, 0f);
        }

        static void Blend(StageCameraRig rig, float p) =>
            rig?.Blend(StagePerformanceConfig.CutInCameraPitchDeg,
                       StagePerformanceConfig.CutInCameraDistance, OutCubic(p));

        static float OutCubic(float p)
        {
            float inv = 1f - Mathf.Clamp01(p);
            return 1f - inv * inv * inv;
        }
    }
}
