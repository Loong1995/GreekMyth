using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 站位布局：固定「阵型组合」驱动（禁止同列前后排同时占位，避免竖向四倍卡高）。
    // 交错阵（方圆 / 却月 / 鹤翼）共用几何：
    //   上侧(B)：后排卡 上缘=队区上界、下缘=前排区下 1/3 线；卡高=该跨度。
    //           前排卡 底缘贴队区内缘（中缝侧），避免同卡高穿入中线。
    //   下侧(A)：镜面对称。
    // 两队可各用不同阵型：逐队 Detect，落点按该队阵型；卡尺取更宽松的一侧。
    // 非已知阵型：回退经典 2×3 格心。
    // =========================================================================

    public enum StanceFormation
    {
        /// <summary>方圆阵：1+5+6（前左 + 后中 + 后右）。</summary>
        FangYuan = 0,
        /// <summary>却月阵：1+2+6（前左 + 前中 + 后右）。</summary>
        QueYue = 1,
        /// <summary>鹤翼阵：2+4+6（前中 + 后左 + 后右）。</summary>
        HeYi = 2,
        /// <summary>仅前排 1~3（或 0~2）单行横排。</summary>
        FrontRow = 3,
        /// <summary>经典 2×3：前 1~3 / 后 4~6 各落半格中心（任意占位兼容）。</summary>
        Grid2x3 = 4,
    }

    public static class StanceLayout
    {
        public const float DesignHalfWidth = 4.6f;
        public const float DesignHalfHeight = 5.2f;
        public const float FrameAspect = 1024f / 1680f;
        public const float RefFrameW = 1.55f;
        public const float RefFrameH = 2.54f;
        const float ChromeFactor = 1.08f;

        public static readonly int[] FangYuanSlots = { 1, 5, 6 };
        public static readonly int[] QueYueSlots = { 1, 2, 6 };
        public static readonly int[] HeYiSlots = { 2, 4, 6 };

        /// <summary>卡尺所用阵型（两队取更宽松侧；交错优先）。</summary>
        public static StanceFormation Formation { get; private set; } = StanceFormation.FangYuan;
        public static StanceFormation FormationA { get; private set; } = StanceFormation.FangYuan;
        public static StanceFormation FormationB { get; private set; } = StanceFormation.FangYuan;
        public static float HalfWidth { get; private set; } = DesignHalfWidth;
        public static float HalfHeight { get; private set; } = DesignHalfHeight;
        public static float RegionWidth { get; private set; }
        public static float CellHeight { get; private set; }
        public static float CardWidth { get; private set; }
        public static float CardHeight { get; private set; }
        public static float RestJitterHalf { get; private set; }
        public static float LayoutScale { get; private set; }
        public static float LineReserve { get; private set; }
        public static float MidClear { get; private set; }

        static float _midEdge;
        static float _outerTop;
        static float _yFront;
        static float _yBack;
        static float _yFrontFlush;
        static float _yBackBand;

        static StanceLayout() =>
            RecalcForTeams(DesignHalfWidth, DesignHalfHeight,
                StanceFormation.FangYuan, StanceFormation.FangYuan);

        public static bool IsStaggered(StanceFormation f) =>
            f is StanceFormation.FangYuan or StanceFormation.QueYue or StanceFormation.HeYi;

        public static string FormationDisplayName(StanceFormation f) => f switch
        {
            StanceFormation.FangYuan => "方圆阵",
            StanceFormation.QueYue => "却月阵",
            StanceFormation.HeYi => "鹤翼阵",
            StanceFormation.FrontRow => "前列横排",
            StanceFormation.Grid2x3 => "六区格心",
            _ => f.ToString(),
        };

        /// <summary>根据一队已占站位推断阵型（子集亦算该阵；未知组合回退 Grid2x3）。</summary>
        public static StanceFormation DetectFormation(IEnumerable<int> positions)
        {
            var set = new HashSet<int>();
            foreach (int raw in positions)
                set.Add(Normalize(raw));
            if (set.Count == 0) return StanceFormation.FangYuan;

            bool onlyFront = true;
            foreach (int p in set)
                if (p > 3) { onlyFront = false; break; }
            if (onlyFront) return StanceFormation.FrontRow;

            if (SetEquals(set, FangYuanSlots) || IsSubset(set, FangYuanSlots))
                return StanceFormation.FangYuan;
            if (SetEquals(set, QueYueSlots) || IsSubset(set, QueYueSlots))
                return StanceFormation.QueYue;
            if (SetEquals(set, HeYiSlots) || IsSubset(set, HeYiSlots))
                return StanceFormation.HeYi;
            return StanceFormation.Grid2x3;
        }

        /// <summary>卡尺：任一方交错 → 交错带；双方皆前列 → 前列；否则格心。</summary>
        public static StanceFormation SizingFormation(StanceFormation a, StanceFormation b)
        {
            if (IsStaggered(a) || IsStaggered(b)) return StanceFormation.FangYuan;
            if (a == StanceFormation.FrontRow && b == StanceFormation.FrontRow)
                return StanceFormation.FrontRow;
            return StanceFormation.Grid2x3;
        }

        static bool SetEquals(HashSet<int> set, int[] slots)
        {
            if (set.Count != slots.Length) return false;
            foreach (int s in slots)
                if (!set.Contains(s)) return false;
            return true;
        }

        static bool IsSubset(HashSet<int> set, int[] slots)
        {
            foreach (int p in set)
            {
                bool ok = false;
                foreach (int s in slots)
                    if (s == p) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        public static void RecalcFromCamera(Camera cam, StanceFormation formA, StanceFormation formB)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null) CameraFitter.EnsureOn(cam);
            RecalcForTeams(DesignHalfWidth, DesignHalfHeight, formA, formB);
        }

        /// <summary>兼容单阵型调用（两队同阵）。</summary>
        public static void RecalcFromCamera(Camera cam = null,
            StanceFormation formation = StanceFormation.FangYuan) =>
            RecalcFromCamera(cam, formation, formation);

        public static void RecalcForTeams(float halfW, float halfH,
            StanceFormation formA, StanceFormation formB)
        {
            FormationA = formA;
            FormationB = formB;
            Recalc(halfW, halfH, SizingFormation(formA, formB));
        }

        public static void Recalc(float halfW, float halfH, StanceFormation formation)
        {
            Formation = formation;
            HalfWidth = halfW;
            HalfHeight = halfH;
            LineReserve = Mathf.Clamp(halfH * 0.10f, 0.42f, 0.95f);
            MidClear = Mathf.Clamp(halfH * 0.055f, 0.28f, 0.65f);
            float colPad = halfW * 0.012f;
            float rowPad = halfH * 0.008f;

            RegionWidth = halfW * 2f / 3f;
            float teamSpan = halfH - LineReserve - MidClear * 0.5f;
            CellHeight = teamSpan * 0.5f;

            _midEdge = MidClear * 0.5f;
            _outerTop = halfH - LineReserve;
            _yFront = _midEdge + CellHeight * 0.5f;
            _yBack = _midEdge + CellHeight * 1.5f;

            float bandBottom = _midEdge + CellHeight / 3f;
            float bandTop = _outerTop;
            float spanBand = bandTop - bandBottom;

            bool staggered = IsStaggered(formation);
            float vSpan = staggered ? spanBand : CellHeight;
            float jMin = Mathf.Min(RegionWidth, vSpan) * 0.02f;
            float maxFrameH = (vSpan - 2f * jMin - rowPad) / ChromeFactor;
            float maxFrameW = RegionWidth - 2f * jMin - colPad;
            CardHeight = Mathf.Min(maxFrameH, maxFrameW / FrameAspect);
            CardWidth = CardHeight * FrameAspect;
            if (CardWidth > maxFrameW)
            {
                CardWidth = maxFrameW;
                CardHeight = CardWidth / FrameAspect;
            }
            CardHeight = Mathf.Max(CardHeight, 0.4f);
            CardWidth = CardHeight * FrameAspect;

            float slackH = vSpan - CardHeight * ChromeFactor - rowPad;
            float slackW = RegionWidth - CardWidth - colPad;
            float jCap = RegionWidth / 10f;
            RestJitterHalf = Mathf.Clamp(
                Mathf.Min(slackH * 0.5f, slackW * 0.5f, jCap), jMin, jCap);
            LayoutScale = CardHeight / RefFrameH;

            float halfCardY = CardHeight * ChromeFactor * 0.5f;
            // 交错 Y 始终算好：异阵对打时逐队选用
            _yBackBand = (bandTop + bandBottom) * 0.5f;
            _yFrontFlush = _midEdge + halfCardY + rowPad * 0.5f;
            float maxFront = _outerTop - halfCardY;
            if (_yFrontFlush > maxFront) _yFrontFlush = maxFront;
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

        /// <summary>区域中心（相对棋盘原点）。teamIdx 0=A 下、1=B 上；按该队阵型落点。</summary>
        public static Vector3 SlotCenter(int teamIdx, int position)
        {
            int p = Normalize(position);
            float x = (ColumnOf(p) - 1) * RegionWidth;
            var form = FormationOfTeam(teamIdx);
            float yAbs;
            if (IsStaggered(form))
                yAbs = IsBackline(p) ? _yBackBand : _yFrontFlush;
            else if (form == StanceFormation.FrontRow)
                yAbs = _yFront;
            else
                yAbs = IsBackline(p) ? _yBack : _yFront;
            float y = teamIdx == 0 ? -yAbs : yAbs;
            return new Vector3(x, y, 0f);
        }
    }
}
