namespace ClientBattle.Units
{
    // =========================================================================
    // 战场分区 + 卡牌尺寸【静态配置】——改数字即调参，重新进 Play 生效。
    // 文档：docs/client/battlefield_layout.md
    // =========================================================================

    public static class BattlefieldLayoutConfig
    {
        // ---- 分区比例（相对地面宽 / 纵深）----

        /// <summary>左右 UI 各占地面宽的比例（0.25 = W/4）。</summary>
        public static float UiSideFraction = 0.25f;

        /// <summary>战场院区占原主战场纵深的比例（0.3＝原 0.2×1.5，远侧过渡天际）。</summary>
        public static float CourtyardDepthFraction = 0.3f;

        /// <summary>隔离带占缩后主战场纵深的比例（0.125 = D/8）。</summary>
        public static float BeltDepthFraction = 0.2f;

        // ---- 地面板 ----

        /// <summary>地天接缝 z（天空板所在；地面远缘）。</summary>
        public static float GroundFarSeamZ = 10f;

        /// <summary>无相机时的设计宽高比（VFX 标准化基准）。</summary>
        public static float DesignAspect = 16f / 9f;

        /// <summary>贴图板相对「正好拍全」的微量外扩（世界单位）。</summary>
        public static float EdgeGuard = 0.05f;

        // ---- 逻辑旋转 ----

        /// <summary>主战场绕缩后中心旋转角（度，正=俯视顺时针，|θ|&lt;90）。</summary>
        public static float RotationDeg = 0f;

        // ---- 卡牌尺寸 ----

        /// <summary>相对格尺反算结果的整体放大。</summary>
        public static float CardScaleBoost = 1.5f;

        /// <summary>卡框含边饰高度放大（贴地/浮空同用）。</summary>
        public static float ChromeFactor = 1.08f;

        /// <summary>站位微抖半径 = 卡宽 × 此值（1/6）。</summary>
        public static float SlotJitterRadiusFactor = 1f / 8f;

        /// <summary>卡框宽高比（美术框 1024×1680）。</summary>
        public static float FrameAspect = 1024f / 1680f;

        /// <summary>Antique 基准框宽（LayoutScale 参考）。</summary>
        public static float RefFrameW = 1.55f;

        /// <summary>Antique 基准框高。</summary>
        public static float RefFrameH = 2.54f;

        // ---- 正交回退 / 安全区（与 CameraFitter 设计区对齐）----

        public static float DesignHalfWidth = 4.6f;
        public static float DesignHalfHeight = 5.2f;

        // ---- 卡牌姿态 ----

        /// <summary>浮空高度 × 卡高（下缘仍贴站位点）。</summary>
        public static float HoverRatio = 0.2f;
    }
}
