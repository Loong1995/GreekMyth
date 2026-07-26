using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】特效尺寸归一（docs/client/vfx_pack_integration.md §三 B）。
    //
    // 厂包特效按 3D 世界尺度做（Effect8 的投影盒 localScale 是 10×5×10），我方卡宽
    // 只有 2 出头。过去每接一件都靠人肉试缩放，试出来的数字散落在各 prefab 里，
    // 既看不出「它想对齐什么」，也没法随布局变化跟着走。
    //
    // 本组件把它变成一句可读的声明：**我要占卡宽（或逻辑圆直径）的多少倍**。
    // 出池时按运行时布局折算，布局变了自动跟随。与 GroundCrackDecal 的
    // CardWidthFactor 同源思路，那边是裂地专用、这边是通用件。
    //
    // 挂在 prefab 根节点。BakedBasis 由标准化工具写入，不要手填。
    // =========================================================================

    public class VfxFitter : MonoBehaviour
    {
        public enum Basis
        {
            /// <summary>对齐卡牌宽度（命中/光环/受击类，绝大多数件用这个）。</summary>
            CardWidth = 0,
            /// <summary>对齐战斗逻辑圆直径（全场级大招）。</summary>
            ArenaDiameter = 1,
            /// <summary>不归一（尺寸另有来源，如裂地自带 CardWidthFactor）。</summary>
            None = 2,
        }

        public Basis Reference = Basis.CardWidth;

        /// <summary>额外倍率。调观感就调这一个数：1 = 保持调参时的观感，
        /// 1.3 = 比当初大三成。</summary>
        public float Factor = 1f;

        /// <summary>调参当时基准量的取值（设计布局下的卡宽/逻辑圆直径）。
        /// 运行期缩放 = 当前基准 / 本值 × Factor，故布局变化时观感占比不变。
        /// 由 `GreekMyth/特效/标准化 尺寸归一` 写入，手改会让缩放失准。</summary>
        public float BakedBasis = 1f;

        Vector3 _baseScale;
        bool _captured;

        void Awake() => Capture();

        void Capture()
        {
            if (_captured) return;
            _baseScale = transform.localScale;
            _captured = true;
        }

        void OnEnable()
        {
            Capture(); // 池化复用时 Awake 只走过一次
            Apply();
        }

        void Apply()
        {
            if (Reference == Basis.None || BakedBasis <= 0.0001f) return;
            float basis = BasisValue();
            if (basis <= 0.0001f) return; // 布局还没算过，保持调参时尺寸
            transform.localScale = _baseScale * (basis * Factor / BakedBasis);
        }

        float BasisValue() => Reference switch
        {
            Basis.CardWidth => Units.StanceLayout.CardWidth,
            Basis.ArenaDiameter => Units.ArenaSlotLayout.CircleRadius * 2f,
            _ => 0f,
        };
    }
}
