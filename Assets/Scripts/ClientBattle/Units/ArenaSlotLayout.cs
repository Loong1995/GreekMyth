using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第4层】站位落点 + 卡脚/定位圆几何。
    // 站位权威：BattlefieldLayout 矩形六等分格心（原站位点）；
    // 实际落点 = 格心为圆心、半径卡宽/6 的圆盘采样（UnitView.RestPosition）。
    // 文档：docs/client/battlefield_layout.md
    // =========================================================================

    public static class ArenaSlotLayout
    {
        /// <summary>装饰圆半径（贴图大圆参考；**不再**用于站位落点）。</summary>
        public const float CircleRadius = 8f;

        /// <summary>地面中心 z（与 BattlefieldLayout / 相机支点同源）。</summary>
        public static float CircleCenterZ => BattlefieldLayout.GroundCenterZ;

        /// <summary>卡牌浮空高度（×卡高）；Config.HoverRatio。</summary>
        static float HoverRatio => BattlefieldLayoutConfig.HoverRatio;

        /// <summary>地面坐标 (x,z) → 卡牌世界锚点（卡心）。
        /// 倾斜后下缘中点落在 (x, 地面, z)。</summary>
        public static Vector3 GroundPoint(float x, float z)
        {
            float cardH = StanceLayout.CardHeight * StanceLayout.ChromeFactor;
            float halfCardH = cardH * 0.5f;
            float rad = CameraFitter.CardPitchDeg * Mathf.Deg2Rad;
            return new Vector3(
                x,
                CameraFitter.PilotGroundY + cardH * HoverRatio + halfCardH * Mathf.Cos(rad),
                z + halfCardH * Mathf.Sin(rad));
        }

        /// <summary>棋盘中心（群攻施法者移动落点）= 隔离带中心上方的卡锚点。</summary>
        public static Vector3 GroundCenter() =>
            GroundPoint(0f, BattlefieldLayout.BeltCenterZ);

        /// <summary>近 3D 地面是否存在（地面特效只在透视舞台播）。</summary>
        public static bool GroundActive => CameraFitter.PerspectivePilot;

        /// <summary>任意世界点正下方的地面点（贴地略抬防 z-fighting）。</summary>
        public static Vector3 GroundUnder(Vector3 worldPos) =>
            new(worldPos.x, CameraFitter.PilotGroundY + 0.05f, worldPos.z);

        /// <summary>卡牌锚点 → 下边缘中点在地面的投影（接地点）。</summary>
        public static Vector3 GroundFoot(Vector3 cardAnchor)
        {
            float halfCardH = StanceLayout.CardHeight * StanceLayout.ChromeFactor * 0.5f;
            float rad = CameraFitter.CardPitchDeg * Mathf.Deg2Rad;
            return GroundUnder(new Vector3(cardAnchor.x, cardAnchor.y,
                cardAnchor.z - halfCardH * Mathf.Sin(rad)));
        }

        /// <summary>卡牌影子（地面足迹）的纵深 = 卡高的竖直投影。</summary>
        public static float CardShadowDepth =>
            StanceLayout.CardHeight * StanceLayout.ChromeFactor
            * Mathf.Sin(CameraFitter.CardPitchDeg * Mathf.Deg2Rad);

        // ------------------------------------------------------------ 两个地面圆
        //
        // 【别再混用】卡牌在地面上有**两个**语义完全不同的圆，2026-07-27 定名：
        //
        //   定位圆 Anchor    = 卡牌**下边缘**（贴地的那条接触线）的端点绕下边缘
        //                      中点转一周画出的圆。
        //                      心＝GroundFoot（接地点＝站位点），**直径＝卡宽**。
        //                      语义是「这张卡站在地上的那一小圈」——脚下的事
        //                      （地面特效、裂地、印记、站位）一律用它。
        //
        //   投影圆 Projection = 卡牌**整张**沿世界竖直投到地面的影子矩形
        //                      （卡宽 × CardShadowDepth）的外接圆。
        //                      心＝卡心正下方，半径＝该矩形半对角线，**比定位圆大**。
        //                      语义是「把整张卡包进去的那一圈」——罩身/绕身件
        //                      要包住整张卡，用它。
        //
        // 后倾 45° 时 CardShadowDepth ≈ 0.707×卡高，卡高≈1.4×卡宽，
        // 故投影圆直径约为定位圆的 1.4 倍——**差得足够大，选错一眼可见**：
        // 拿投影圆做地面痕迹会散出一圈虚边，拿定位圆做罩身会把卡角切出来。
        //
        // 旧名 `CardCircle*` 指的是投影圆，已废止：那个名字读起来像定位圆，
        // 是这次混淆的根源。**不要再引入不带 Anchor/Projection 前缀的圆。**

        /// <summary>定位圆半径 = 卡宽 / 2。</summary>
        public static float AnchorCircleRadius => StanceLayout.CardWidth * 0.5f;

        /// <summary>定位圆直径 = 卡宽。</summary>
        public static float AnchorCircleDiameter => StanceLayout.CardWidth;

        /// <summary>定位圆心 = 下边缘中点在地面的投影（接地点）。
        /// 注意与 <see cref="ProjectionCircleCenter"/> 不同心：前者在卡脚，
        /// 后者在卡心正下方，两者相差 CardShadowDepth/2。</summary>
        public static Vector3 AnchorCircleCenter(Vector3 cardAnchor) => GroundFoot(cardAnchor);

        /// <summary>投影圆半径 = 影子矩形的半对角线。</summary>
        public static float ProjectionCircleRadius
        {
            get
            {
                float w = StanceLayout.CardWidth;
                float d = CardShadowDepth;
                return Mathf.Sqrt(w * w + d * d) * 0.5f;
            }
        }

        public static float ProjectionCircleDiameter => ProjectionCircleRadius * 2f;

        /// <summary>投影圆心 = 卡心正下方（影子矩形的中心）。</summary>
        public static Vector3 ProjectionCircleCenter(Vector3 cardAnchor) => GroundUnder(cardAnchor);

        public static float CardTopY(Vector3 cardAnchor)
        {
            float halfCardH = StanceLayout.CardHeight * StanceLayout.ChromeFactor * 0.5f;
            return cardAnchor.y + halfCardH * Mathf.Cos(CameraFitter.CardPitchDeg * Mathf.Deg2Rad);
        }

        /// <summary>队伍站位落点（透视）：格心 → GroundPoint（下缘贴格心）。</summary>
        public static Vector3 SlotCenter(int teamIdx, int position)
        {
            BattlefieldLayout.SlotCenterXZ(teamIdx, position, out float x, out float z);
            if (Mathf.Abs(CameraFitter.PilotYawDeg) > 0.01f)
            {
                var twisted = Quaternion.Euler(0f, CameraFitter.PilotYawDeg, 0f)
                    * new Vector3(x, 0f, z - BattlefieldLayout.MainCenterZ);
                x = twisted.x;
                z = BattlefieldLayout.MainCenterZ + twisted.z;
            }
            return GroundPoint(x, z);
        }
    }
}
