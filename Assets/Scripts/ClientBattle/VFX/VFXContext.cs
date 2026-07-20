using ClientBattle.Audio;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 演出上下文：SkillPerformance.Play 的运行环境。
    // 提供按 unitId 查 Transform/UnitView 的方法、VFXManager、CameraShaker、
    // 飘字/音效/气泡服务与棋盘（中心点、整盘滤镜挂点）。
    // =========================================================================

    public class VFXContext
    {
        public BattleBoardView Board;
        public VFXManager Vfx;
        public FloatingTextService Floats;
        public SfxManager Sfx;
        public ChatBubbleService Bubbles;
        /// <summary>播放速度倍率（1=常速）；演出内所有等待时长除以它。</summary>
        public float SpeedScale = 1f;
        /// <summary>组内节拍倍率（B1 连发加速用；Runner 每组播放前设置、播完复位）。</summary>
        public float TempoScale = 1f;
        /// <summary>全局时长倍率（&gt;1 放慢）：动画节拍与 Runner 停顿共用，便于看清战报。</summary>
        public float DurationMul = 2f;
        /// <summary>每条性格台词占用的时间轴秒数（弹气泡后等待；再经 Scaled）。</summary>
        public float TraitLinePauseSeconds = 0.5f;

        public UnitView Unit(string heroId) => Board.Unit(heroId);
        public Transform UnitTransform(string heroId) => Board.UnitTransform(heroId);
        public Vector3 BoardCenter => Board.Center;

        /// <summary>速度换算后的等待时长（含 DurationMul）。</summary>
        public float Scaled(float seconds) =>
            seconds * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, SpeedScale * TempoScale);

        public void Shake(float strength, float duration) =>
            CameraShaker.Shake(strength,
                duration * Mathf.Max(0.1f, DurationMul) / Mathf.Max(0.1f, SpeedScale));
    }
}
