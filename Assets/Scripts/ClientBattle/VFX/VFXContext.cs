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

        public UnitView Unit(string heroId) => Board.Unit(heroId);
        public Transform UnitTransform(string heroId) => Board.UnitTransform(heroId);
        public Vector3 BoardCenter => Board.Center;

        /// <summary>速度换算后的等待时长。</summary>
        public float Scaled(float seconds) => seconds / Mathf.Max(0.1f, SpeedScale);

        public void Shake(float strength, float duration) =>
            CameraShaker.Shake(strength, duration / Mathf.Max(0.1f, SpeedScale));
    }
}
