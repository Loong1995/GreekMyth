using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【战场分区权威】地面贴图 → UI 侧栏 + 缩后主战场 + 战场院区 + 隔离带 → 六等分。
    // 文档：docs/client/battlefield_layout.md
    //
    // 调参：BattlefieldLayoutConfig（静态配置，改数字重新 Play）。
    // =========================================================================

    public static class BattlefieldLayout
    {
        public static float UiSideFraction => BattlefieldLayoutConfig.UiSideFraction;
        public static float CourtyardDepthFraction => BattlefieldLayoutConfig.CourtyardDepthFraction;
        public static float BeltDepthFraction => BattlefieldLayoutConfig.BeltDepthFraction;
        public static float GroundFarSeamZ => BattlefieldLayoutConfig.GroundFarSeamZ;
        public static float DesignAspect => BattlefieldLayoutConfig.DesignAspect;
        public static float EdgeGuard => BattlefieldLayoutConfig.EdgeGuard;

        static float _aspect = float.NaN;
        static float _nearZ;
        static float _halfWidth;

        /// <summary>配置变更后清分区缓存（改 Config 数字后下次 Ensure 会重算）。</summary>
        public static void InvalidateCache() => _aspect = float.NaN;

        /// <summary>按宽高比解析反算「正好拍全」的地面矩形（缓存）。</summary>
        public static void Recalc(float aspect)
        {
            if (aspect <= 0f || float.IsNaN(aspect)) aspect = DesignAspect;
            if (Mathf.Approximately(aspect, _aspect)) return;
            _aspect = aspect;

            float pitch = CameraFitter.PilotPitchDeg * Mathf.Deg2Rad;
            float dist = CameraFitter.PilotDistance;
            float half = CameraFitter.PilotFovFor(aspect) * 0.5f * Mathf.Deg2Rad;
            float camY = Mathf.Sin(pitch) * dist;
            float camZ = -Mathf.Cos(pitch) * dist;
            float dy = camY - CameraFitter.PilotGroundY;

            _nearZ = camZ + dy / Mathf.Tan(pitch + half);

            float axialDepth = Mathf.Sin(pitch) * dy
                + Mathf.Cos(pitch) * (GroundFarSeamZ - camZ);
            _halfWidth = Mathf.Tan(half) * aspect * axialDepth;
        }

        public static void RecalcFromCamera(Camera cam) =>
            Recalc(cam != null ? cam.aspect : DesignAspect);

        static void Ensure()
        {
            if (float.IsNaN(_aspect)) Recalc(DesignAspect);
        }

        // ---------------- 地面贴图区（动态） ----------------

        public static float GroundNearZ { get { Ensure(); return _nearZ; } }
        public static float GroundFarZ => GroundFarSeamZ;
        public static float GroundHalfWidth { get { Ensure(); return _halfWidth; } }
        public static float GroundWidth => GroundHalfWidth * 2f;
        public static float GroundDepth => GroundFarZ - GroundNearZ;
        public static float GroundCenterZ => (GroundNearZ + GroundFarZ) * 0.5f;

        // ---------------- 战场院区（原主战场远侧横条） ----------------

        /// <summary>原逻辑主战场纵深（= 地面贴图纵深；院区按此 D 抽 D/5）。</summary>
        public static float OriginalMainDepth => GroundDepth;

        /// <summary>战场院区纵深 = 原主战场 D / 5。</summary>
        public static float CourtyardDepth =>
            OriginalMainDepth * CourtyardDepthFraction;

        /// <summary>缩后逻辑主战场纵深 = 原 D × 4/5（站位/隔离带所在）。</summary>
        public static float MainDepth =>
            OriginalMainDepth * (1f - CourtyardDepthFraction);

        /// <summary>缩后主战场近/远缘（θ=0 世界 z）。</summary>
        public static float MainNearZ => GroundNearZ;
        public static float MainFarZ => GroundFarZ - CourtyardDepth;
        public static float MainCenterZ => (MainNearZ + MainFarZ) * 0.5f;

        /// <summary>院区近/远缘（θ=0）：主战场远缘 → 地天接缝。</summary>
        public static float CourtyardNearZ => MainFarZ;
        public static float CourtyardFarZ => GroundFarZ;

        // ---------------- 逻辑旋转（绕缩后主战场中心；角度在 Config 里调） ----------------

        /// <summary>主战场旋转角（度）。读写 BattlefieldLayoutConfig.RotationDeg。</summary>
        public static float RotationDeg
        {
            get => Mathf.Clamp(BattlefieldLayoutConfig.RotationDeg, -89.9f, 89.9f);
            set
            {
                BattlefieldLayoutConfig.RotationDeg = Mathf.Clamp(value, -89.9f, 89.9f);
                InvalidateCache();
            }
        }

        static float RotRad => RotationDeg * Mathf.Deg2Rad;

        /// <summary>旋转系 (u,v) → 世界 (x,z)；原点 = MainCenterZ。</summary>
        public static void LocalToWorld(float u, float v, out float x, out float z)
        {
            float c = Mathf.Cos(RotRad), s = Mathf.Sin(RotRad);
            x = u * c + v * s;
            z = MainCenterZ + (-u * s + v * c);
        }

        // ---------------- 分区（仅缩后主战场） ----------------

        public static float MainHalfWidth =>
            GroundHalfWidth * (1f - 2f * UiSideFraction);

        /// <summary>隔离带半厚（相对缩后主战场）；中心 = MainCenterZ。</summary>
        public static float BeltHalfDepth => MainDepth * BeltDepthFraction * 0.5f;
        public static float BeltCenterZ => MainCenterZ;

        /// <summary>修正后站位区半纵深；θ=0 时 = MainDepth/2。</summary>
        public static float StanceHalfDepth
        {
            get
            {
                Ensure();
                float halfMain = MainDepth * 0.5f;
                float v = (halfMain - MainHalfWidth * Mathf.Abs(Mathf.Sin(RotRad)))
                          / Mathf.Cos(RotRad);
                return Mathf.Max(v, BeltHalfDepth * 1.5f);
            }
        }

        public static float TeamStanceWidth => MainHalfWidth * 2f;
        public static float TeamStanceDepth => StanceHalfDepth - BeltHalfDepth;
        public static float CellWidth => TeamStanceWidth / 3f;
        public static float CellDepth => TeamStanceDepth / 2f;

        /// <summary>θ=0 站位半纵深（卡尺锁定）。</summary>
        public static float StanceHalfDepthAtZero => MainDepth * 0.5f;
        public static float CardCellWidth => TeamStanceWidth / 3f;
        public static float CardCellDepth =>
            (StanceHalfDepthAtZero - BeltHalfDepth) * 0.5f;

        /// <summary>队矩形（旋转系局部）；teamIdx 0=A 近、1=B 远。</summary>
        public static void TeamStanceBounds(int teamIdx,
            out float minU, out float maxU, out float minV, out float maxV)
        {
            minU = -MainHalfWidth;
            maxU = MainHalfWidth;
            if (teamIdx == 0)
            {
                minV = -StanceHalfDepth;
                maxV = -BeltHalfDepth;
            }
            else
            {
                minV = BeltHalfDepth;
                maxV = StanceHalfDepth;
            }
        }

        /// <summary>站位格心世界 (x,z)。</summary>
        public static void SlotCenterXZ(int teamIdx, int position, out float x, out float z)
        {
            int p = StanceLayout.Normalize(position);
            int col = StanceLayout.ColumnOf(p);
            bool back = StanceLayout.IsBackline(p);

            TeamStanceBounds(teamIdx, out float minU, out float maxU, out float minV, out float maxV);
            float cellW = (maxU - minU) / 3f;
            float cellD = (maxV - minV) / 2f;
            float u = minU + (col + 0.5f) * cellW;
            float v = teamIdx == 0
                ? (back ? minV + 0.5f * cellD : minV + 1.5f * cellD)
                : (back ? maxV - 0.5f * cellD : maxV - 1.5f * cellD);

            LocalToWorld(u, v, out x, out z);
        }
    }
}
