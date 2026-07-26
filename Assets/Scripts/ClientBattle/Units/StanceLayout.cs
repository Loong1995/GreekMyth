using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 站位编号 / 阵型识别 / 卡尺（单体制）。
    // 落点几何权威：BattlefieldLayout（矩形六等分）；本类负责 1–6 语义与卡尺寸。
    // 文档：docs/client/battlefield_layout.md 、 docs/mechanics/formations.md
    // =========================================================================

    /// <summary>预设阵型（精确站位集合匹配；None = 未命中任何预设）。</summary>
    public enum StanceFormation
    {
        None = 0,
        /// <summary>一字阵 {1,2,3}</summary>
        Yizi = 1,
        /// <summary>锥形阵 {2,4,6}</summary>
        Zhui = 2,
        /// <summary>箕形阵 {1,5,6}</summary>
        Ji = 3,
        /// <summary>方圆阵 {3,4,5}</summary>
        FangYuan = 4,
        /// <summary>偃月阵 {1,3,5}</summary>
        Yanyue = 5,
        /// <summary>雁行阵 {1,2,6}</summary>
        Yanxing = 6,
    }

    public static class StanceLayout
    {
        public const float FrameAspectFallback = 1024f / 1680f;
        public const float RefFrameWFallback = 1.55f;
        public const float RefFrameHFallback = 2.54f;

        public static float DesignHalfWidth => BattlefieldLayoutConfig.DesignHalfWidth;
        public static float DesignHalfHeight => BattlefieldLayoutConfig.DesignHalfHeight;
        public static float FrameAspect =>
            BattlefieldLayoutConfig.FrameAspect > 0.01f
                ? BattlefieldLayoutConfig.FrameAspect : FrameAspectFallback;
        public static float RefFrameW =>
            BattlefieldLayoutConfig.RefFrameW > 0.01f
                ? BattlefieldLayoutConfig.RefFrameW : RefFrameWFallback;
        public static float RefFrameH =>
            BattlefieldLayoutConfig.RefFrameH > 0.01f
                ? BattlefieldLayoutConfig.RefFrameH : RefFrameHFallback;
        public static float ChromeFactor => Mathf.Max(1f, BattlefieldLayoutConfig.ChromeFactor);
        public static float CardScaleBoost => Mathf.Max(0.1f, BattlefieldLayoutConfig.CardScaleBoost);

        public static readonly int[] YiziSlots = { 1, 2, 3 };
        public static readonly int[] ZhuiSlots = { 2, 4, 6 };
        public static readonly int[] JiSlots = { 1, 5, 6 };
        public static readonly int[] FangYuanSlots = { 3, 4, 5 };
        public static readonly int[] YanyueSlots = { 1, 3, 5 };
        public static readonly int[] YanxingSlots = { 1, 2, 6 };

        public static StanceFormation Formation { get; private set; } = StanceFormation.None;
        public static StanceFormation FormationA { get; private set; } = StanceFormation.None;
        public static StanceFormation FormationB { get; private set; } = StanceFormation.None;
        public static float HalfWidth { get; private set; } = 4.6f;
        public static float HalfHeight { get; private set; } = 5.2f;
        public static float RegionWidth { get; private set; }
        public static float CellHeight { get; private set; }
        public static float CardWidth { get; private set; }
        public static float CardHeight { get; private set; }
        public static float RestJitterHalf { get; private set; }
        public static float LayoutScale { get; private set; }
        public static float LineReserve { get; private set; }
        public static float MidClear { get; private set; }

        static StanceLayout() => Recalc(DesignHalfWidth, DesignHalfHeight);

        public static string FormationDisplayName(StanceFormation f) => f switch
        {
            StanceFormation.Yizi => "一字阵",
            StanceFormation.Zhui => "锥形阵",
            StanceFormation.Ji => "箕形阵",
            StanceFormation.FangYuan => "方圆阵",
            StanceFormation.Yanyue => "偃月阵",
            StanceFormation.Yanxing => "雁行阵",
            _ => "无阵型",
        };

        /// <summary>服务端 formation_id 与枚举对齐。</summary>
        public static string FormationId(StanceFormation f) => f switch
        {
            StanceFormation.Yizi => "yizi",
            StanceFormation.Zhui => "zhui",
            StanceFormation.Ji => "ji",
            StanceFormation.FangYuan => "fangyuan",
            StanceFormation.Yanyue => "yanyue",
            StanceFormation.Yanxing => "yanxing",
            _ => "",
        };

        /// <summary>精确集合相等才算命中预设阵型；否则 None。</summary>
        public static StanceFormation DetectFormation(IEnumerable<int> positions)
        {
            var set = new HashSet<int>();
            foreach (int raw in positions)
                set.Add(Normalize(raw));
            if (SetEquals(set, YiziSlots)) return StanceFormation.Yizi;
            if (SetEquals(set, ZhuiSlots)) return StanceFormation.Zhui;
            if (SetEquals(set, JiSlots)) return StanceFormation.Ji;
            if (SetEquals(set, FangYuanSlots)) return StanceFormation.FangYuan;
            if (SetEquals(set, YanyueSlots)) return StanceFormation.Yanyue;
            if (SetEquals(set, YanxingSlots)) return StanceFormation.Yanxing;
            return StanceFormation.None;
        }

        static bool SetEquals(HashSet<int> set, int[] slots)
        {
            if (set.Count != slots.Length) return false;
            foreach (int s in slots)
                if (!set.Contains(s)) return false;
            return true;
        }

        public static void RecalcFromCamera(Camera cam, StanceFormation formA, StanceFormation formB)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null) CameraFitter.EnsureOn(cam);
            BattlefieldLayout.RecalcFromCamera(cam); // 地面板随宽高比刷新
            FormationA = formA;
            FormationB = formB;
            Formation = formA != StanceFormation.None ? formA
                : formB != StanceFormation.None ? formB : StanceFormation.None;
            Recalc(DesignHalfWidth, DesignHalfHeight);
        }

        public static void RecalcFromCamera(Camera cam = null,
            StanceFormation formation = StanceFormation.None) =>
            RecalcFromCamera(cam, formation, formation);

        public static void RecalcForTeams(float halfW, float halfH,
            StanceFormation formA, StanceFormation formB)
        {
            FormationA = formA;
            FormationB = formB;
            Formation = formA != StanceFormation.None ? formA
                : formB != StanceFormation.None ? formB : StanceFormation.None;
            Recalc(halfW, halfH);
        }

        /// <summary>单体制卡尺：按 θ=0 格尺寸反算卡宽高（旋转只改落点，不改卡尺）。</summary>
        public static void Recalc(float halfW, float halfH)
        {
            HalfWidth = halfW;
            HalfHeight = halfH;
            // 卡尺锁定 θ=0 格尺寸；halfW/halfH 保留兼容相机安全区。
            RegionWidth = BattlefieldLayout.CardCellWidth;
            CellHeight = BattlefieldLayout.CardCellDepth;
            LineReserve = 0f;
            MidClear = BattlefieldLayout.BeltHalfDepth * 2f;

            float colPad = RegionWidth * 0.06f;
            float rowPad = CellHeight * 0.06f;
            float jMin = Mathf.Min(RegionWidth, CellHeight) * 0.02f;
            float maxFrameH = (CellHeight - 2f * jMin - rowPad) / ChromeFactor;
            float maxFrameW = RegionWidth - 2f * jMin - colPad;
            CardHeight = Mathf.Min(maxFrameH, maxFrameW / FrameAspect);
            CardWidth = CardHeight * FrameAspect;
            if (CardWidth > maxFrameW)
            {
                CardWidth = maxFrameW;
                CardHeight = CardWidth / FrameAspect;
            }
            CardHeight = Mathf.Max(CardHeight, 0.4f) * CardScaleBoost;
            CardWidth = CardHeight * FrameAspect;

            // 站位微抖半径固定为卡宽/6（圆盘采样）；旧 slack 方框抖动废弃。
            RestJitterHalf = SlotJitterRadius;
            LayoutScale = CardHeight / RefFrameH;
        }

        /// <summary>站位微抖圆盘半径 = 卡宽 × Config.SlotJitterRadiusFactor。</summary>
        public static float SlotJitterRadius =>
            Mathf.Max(CardWidth, 0.01f)
            * Mathf.Clamp01(BattlefieldLayoutConfig.SlotJitterRadiusFactor);

        /// <summary>在圆心 (cx,cy) 半径 SlotJitterRadius 的圆盘内均匀采样偏移 (dx,dy)。</summary>
        public static void SampleSlotDiskOffset(out float dx, out float dy)
        {
            float r = SlotJitterRadius * Mathf.Sqrt(Random.value);
            float a = Random.value * (Mathf.PI * 2f);
            dx = r * Mathf.Cos(a);
            dy = r * Mathf.Sin(a);
        }

        public static int Normalize(int position)
        {
            if (position >= 1 && position <= 6) return position;
            if (position >= 0 && position <= 2) return position + 1;
            return 2;
        }

        public static int ColumnOf(int position) => (Normalize(position) - 1) % 3;

        public static bool IsBackline(int position) => Normalize(position) >= 4;

        public static StanceFormation FormationOfTeam(int teamIdx) =>
            teamIdx == 0 ? FormationA : FormationB;

        /// <summary>正交回退：XZ 格心映到 XY 平面。纵深按比例压进安全区高度
        /// （地面板纵深约 13 > 安全区 10.4，直映会出画），横向同比压进安全区宽。</summary>
        public static Vector3 SlotCenter(int teamIdx, int position)
        {
            BattlefieldLayout.SlotCenterXZ(teamIdx, position, out float x, out float z);
            float ky = DesignHalfHeight * 2f * 0.9f / BattlefieldLayout.MainDepth;
            float kx = Mathf.Min(1f, DesignHalfWidth / BattlefieldLayout.MainHalfWidth);
            return new Vector3(x * kx, (z - BattlefieldLayout.MainCenterZ) * ky, 0f);
        }
    }
}
