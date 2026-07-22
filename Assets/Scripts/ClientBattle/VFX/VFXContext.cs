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

        // ---- 编排层回调（由 Runner 注入；演出执行层不得反向引用 Runner 单例）----
        /// <summary>伤害结算回调（高伤 cut-in 门槛判定在编排层）。</summary>
        public System.Action<Events.DamageEvent, string> OnDamageSettled;
        /// <summary>cut-in 请求 (heroId, text, groupId)：heroId 非空走全屏单人
        /// cut-in（CutInService），空走文字横幅回退；去重在 CutInService。</summary>
        public System.Action<string, string, int> OnCutInRequested;
        /// <summary>顶部横幅（BannerService.Set 的注入；单挑等演出内更新横幅用）。</summary>
        public System.Action<string> OnBanner;
        /// <summary>本组出手前刚播完满档 cut-in（势能全开）：攻击主音效改用
        /// 强化版 sfx_attack_empowered。Runner 每组播放前设置、播完复位。</summary>
        public bool EmpoweredStrike;

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
