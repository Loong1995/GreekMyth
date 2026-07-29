using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 裂地表现语言的**唯一颜色/参数真源**（docs/client/ground_crack_language.md §三）。
    //
    // 结构是**两个正交维度**（2026-07-26 重组，别再混成一张「档位表」）：
    //
    //   模式 Mode（2 类）  = 裂缝**形状骨架**：用哪张遮罩、往哪长、要不要锁朝向、
    //                       带几道毛刺、尺寸基准怎么算。弹道类 / 命中类。
    //   强度 Strength（3 档）= 同一骨架的**表现烈度**：缝宽、持续、亮度。
    //   面积 area（调用参数）= 命中类额外的大小倍率（拉满出手 ×1.5）。
    //
    // 于是「哪种形状」与「多猛」彼此独立：加一个新场景只需选 模式 + 强度 + 面积，
    // 不再新增 prefab、不再新增一套参数。
    //
    // 红线：裂地颜色禁止在特效 prefab 里各自写死；两个模式共用本表。
    // 碎块（L3）的颜色不在这里：它从舞台底图 arena_<stage>.png 现切
    // （GroundChunkBaker），所以换底图/换舞台自动跟色，构造上不可能不协调。
    // =========================================================================

    public static class GroundCrackPalette
    {
        /// <summary>L1 裂缝：近黑 + 极轻暖偏，压在亮色大理石上才读得出线条。</summary>
        public static readonly Color Crack = new Color(0.055f, 0.045f, 0.042f, 1f);

        /// <summary>L2 缝底：同色系亮度 ×0.4，制造「缝里有深处」的层次。</summary>
        public static readonly Color CrackCore = new Color(0.022f, 0.018f, 0.017f, 1f);

        /// <summary>尘雾：取底图平均色的替代值（灰白偏暖），半透明叠加。</summary>
        public static readonly Color Dust = new Color(0.72f, 0.69f, 0.63f, 1f);

        /// <summary>冲击环：比裂缝略亮，作瞬时高光边。</summary>
        public static readonly Color Shock = new Color(0.30f, 0.27f, 0.24f, 1f);

        /// <summary>熔岩：裂开瞬间锋面的过曝色。分量 &gt;1 是 HDR，靠 BattlePostFx
        /// 的 Bloom（阈值 0.85）溢出成光晕 —— 这是「酷炫」与「朴素」的分界，
        /// 压回 1 以内就只是一条橙线。偏红不偏黄，避免与宙斯的金雷撞色。
        ///
        /// 色相取自 Magic Pack v1 / Effect8 的 `Decal1.mat _TintColor`
        /// (1.5, 0.386, 0.077)，等比抬亮 —— 这是「参考 Effect8 观感」里
        /// **可直接迁移**的那部分（vfx_standardization §二 逐层去向）。</summary>
        public static readonly Color Lava = new Color(2.4f, 0.55f, 0.12f, 1f);

        /// <summary>贴花排序：地面（不透明几何）之上、卡牌（sortingOrder≥0）之下。</summary>
        public const int SortingOrder = -50;

        /// <summary>贴花离地高度，防与地面网格 z-fighting。</summary>
        public const float LiftY = 0.02f;

        /// <summary>裂缝淡入时长（与强度无关，三档统一起步）。</summary>
        public const float FadeIn = 0.08f;

        /// <summary>碎块抛飞落地的富余时间，避免裂缝没了石头还在空中被回收。</summary>
        public const float ChunkTail = 0.35f;

        /// <summary>尘雾排序：与裂缝同处地面层，**必须低于卡牌**。
        /// 早期给了 44（VFXManager 的空中特效下限档），结果尘雾糊在卡面前把
        /// 英雄立绘整片压灰（2026-07-25 实测）。</summary>
        public const int DustSortingOrder = SortingOrder + 2;

        // ====================================================================
        // 维度一：强度三档（缝宽 / 持续 / 亮度）
        // ====================================================================

        /// <summary>强度档。三档是**台阶**，不是三个可任意调的数值组：
        /// 普通打击只留暗缝微光，高规格出手缝更宽更亮，最高档到熔岩过曝。</summary>
        public enum Strength
        {
            /// <summary>档1 轻：细缝、短存续、缝底一点余烬。</summary>
            Light = 1,
            /// <summary>档2 重：缝变宽、存续拉长、锋面成型（看得出烧红的缝）。</summary>
            Heavy = 2,
            /// <summary>档3 熔岩：最宽最久，Bloom 大幅溢出，并叠熔岩层。</summary>
            Blaze = 3,
        }

        /// <summary>一档强度的全部配置。缝宽/持续/亮度都在这里，
        /// **禁止**在别处给某一档单独填数（那会让台阶失去意义）。</summary>
        public readonly struct StrengthSpec
        {
            /// <summary>缝宽增益（喂 shader `_MaskGain`）：把遮罩 alpha 整体抬高，
            /// 细线变粗、梢部更多地长出来 —— 比缩放面片更像「缝更宽」，
            /// 且不会把放射骨架拉成椭圆。</summary>
            public readonly float MaskGain;
            /// <summary>裂缝驻留。</summary>
            public readonly float Hold;
            /// <summary>裂缝淡出（亮光与它同步消失，见 GroundCrackDecal.Glow）。</summary>
            public readonly float FadeOut;
            /// <summary>熔岩锋面峰值（HDR，喂 `_GlowIntensity`）。</summary>
            public readonly float GlowPeak;
            /// <summary>锋面亮带宽度（`_FrontWidth`）。越宽越像整条缝在烧。</summary>
            public readonly float FrontWidth;
            /// <summary>锋面扫过后缝底的余烬保底（`_EmberFloor`）。</summary>
            public readonly float EmberFloor;
            /// <summary>骨架整体放大倍率。高档「更猛」的一半靠**同一骨架摊得更大**
            /// （裂得更远），另一半才靠缝宽/亮度 —— 只堆亮度会变成一块发光贴图。</summary>
            public readonly float SizeScale;

            public StrengthSpec(float maskGain, float hold, float fadeOut,
                                float glowPeak, float frontWidth, float emberFloor,
                                float sizeScale)
            {
                MaskGain = maskGain; Hold = hold; FadeOut = fadeOut;
                GlowPeak = glowPeak; FrontWidth = frontWidth; EmberFloor = emberFloor;
                SizeScale = sizeScale;
            }

            /// <summary>实例存活时长（传给 VFXManager.PlayAt 的 duration）。
            /// 盖住裂缝淡出；碎块层已取消，仍留一点尾以免淡出被截断。</summary>
            public float Duration => FadeIn + Hold + FadeOut + ChunkTail;
        }

        // 亮度/缝宽按档；持续时间按「模式 × 档」——弹道最高档＝最低档 ×1.5，
        // 命中最高档＝最低档 ×2（中间档线性插值）。基线 Hold/FadeOut 取档 1。
        const float LightHold = 0.8f;
        const float LightFadeOut = 0.45f;

        static readonly float[] PathDurationScale = { 1f, 1.25f, 1.5f };   // Light/Heavy/Blaze
        static readonly float[] ImpactDurationScale = { 1f, 1.5f, 2f };

        static StrengthSpec BuildSpec(Strength strength, Mode mode)
        {
            int i = Mathf.Clamp((int)strength - 1, 0, 2);
            float scale = mode == Mode.Path ? PathDurationScale[i] : ImpactDurationScale[i];
            float hold = LightHold * scale;
            float fade = LightFadeOut * scale;
            // 缝宽两模式共用；熔岩三档全开，弹道同档比命中稍弱（×0.78）
            const float pathLava = 0.78f;
            bool path = mode == Mode.Path;
            float L(float impact) => path ? impact * pathLava : impact;
            return strength switch
            {
                Strength.Light => new StrengthSpec(1.15f, hold, fade,
                    L(2.1f), 0.14f, L(0.12f), 1.0f),
                Strength.Heavy => new StrengthSpec(2.55f, hold, fade,
                    L(3.6f), 0.20f, L(0.28f), 1.0f),
                _ => new StrengthSpec(3.8f, hold, fade,
                    L(4.4f), 0.24f, L(0.34f), 1.35f),
            };
        }

        public static StrengthSpec SpecOf(Strength strength, Mode mode = Mode.Path) =>
            BuildSpec(strength, mode);

        // 注：最高档曾额外叠厂包熔岩层 `ground_lava_bloom`（Effect8 晋升件）。
        // 它是一张自带形状的独立贴图，压在自研裂缝上就成了「地上贴了块发光图」，
        // 与档 1/2 的自然裂缝断档，2026-07-26 人工验收打回后取消。
        // 熔岩观感改由 shader 沿缝渐变 + 骨架放大承担；晋升件本身保留在库里备用。

        // ====================================================================
        // 维度二：模式两类（裂缝形状骨架）
        // ====================================================================

        public enum Mode
        {
            /// <summary>弹道类：沿弹道方向的大小缝混排遮罩，朝向由调用方给。</summary>
            Path = 0,
            /// <summary>命中类：受击点为中心的分形放射裂纹，随机自旋；尺寸按卡宽定。</summary>
            Impact = 1,
        }

        /// <summary>一个模式的骨架规格：只管形状与尺寸基准，不含任何烈度参数。</summary>
        public readonly struct ModeSpec
        {
            /// <summary>所属模式（持续时长按模式分档时用）。</summary>
            public readonly Mode Kind;
            public readonly string Key;
            /// <summary>烘制基准尺寸（世界）。命中类是直径，弹道类是 长×宽。</summary>
            public readonly float BakedLength, BakedWidth;
            /// <summary>0=从中心向外扩（命中类），1=沿 +u 推进（弹道类沿弹道方向）。</summary>
            public readonly float GrowthMode;
            /// <summary>裂缝推进到满的时长。</summary>
            public readonly float GrowTime;
            /// <summary>true＝朝向由调用方给（弹道类），false＝出场随机自旋。</summary>
            public readonly bool Oriented;
            /// <summary>主缝两侧的毛刺分叉数（弹道类才有：单一长条读起来像一道划痕）。</summary>
            public readonly int Spurs;
            /// <summary>&gt;0 时直径改由「卡牌宽度 × 本系数 × 面积倍率」定
            /// （命中类）。卡宽随分辨率自适配算出，编辑期拿不到，只能运行期定。</summary>
            public readonly float CardWidthFactor;
            /// <summary>碎块粒子数（烘制期定）。0＝不烘碎块（俯视下像漂浮烟雾）。</summary>
            public readonly int ChunkCount;
            /// <summary>是否带尘雾层。</summary>
            public readonly bool Dust;

            public ModeSpec(Mode kind, string key, float bakedLength, float bakedWidth,
                            float growthMode, float growTime, bool oriented,
                            int spurs, float cardWidthFactor, int chunkCount, bool dust)
            {
                Kind = kind; Key = key; BakedLength = bakedLength; BakedWidth = bakedWidth;
                GrowthMode = growthMode; GrowTime = growTime; Oriented = oriented;
                Spurs = spurs; CardWidthFactor = cardWidthFactor;
                ChunkCount = chunkCount; Dust = dust;
            }
        }

        /// <summary>弹道类骨架：若干随机大缝 + 若干随机小缝（一张遮罩混排），
        /// 朝向由弹道 yaw 锁定；不再挂独立毛刺面片。
        /// Key 是兼容别名；实际出场从 <see cref="PathVariantKeys"/> 随机抽。</summary>
        public static readonly ModeSpec PathMode =
            new ModeSpec(Mode.Path, "ground_crack_path", bakedLength: 2.5f, bakedWidth: 1.05f,
                         growthMode: 1f, growTime: 0.22f, oriented: true,
                         spurs: 0, cardWidthFactor: 0f, chunkCount: 0, dust: false);

        /// <summary>弹道遮罩变体（G4 烘出）。单遮罩再怎么 Hash 也是一张图，
        /// 每次出手同一张 → 「永远两道大缝」；出场随机抽一套。</summary>
        public static readonly string[] PathVariantKeys =
        {
            "ground_crack_path_0", "ground_crack_path_1",
            "ground_crack_path_2", "ground_crack_path_3",
        };

        /// <summary>抽一套弹道骨架。专配 key 优先；否则在变体里随机。</summary>
        public static string PickPathKey(string configured)
        {
            if (!string.IsNullOrEmpty(configured)) return configured;
            return PathVariantKeys[UnityEngine.Random.Range(0, PathVariantKeys.Length)];
        }

        /// <summary>同一条弹道的多段裂地各抽不同变体（尽量不复读同一张）。
        /// 专配 key 时整条弹道仍用专配。</summary>
        public static string[] PickPathKeys(string configured, int count)
        {
            var keys = new string[Mathf.Max(0, count)];
            if (keys.Length == 0) return keys;
            if (!string.IsNullOrEmpty(configured))
            {
                for (int i = 0; i < keys.Length; i++) keys[i] = configured;
                return keys;
            }
            // 洗牌后按序取；段数多于变体时再绕回，至少相邻段尽量不同
            var pool = (string[])PathVariantKeys.Clone();
            for (int i = pool.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            for (int i = 0; i < keys.Length; i++)
                keys[i] = pool[i % pool.Length];
            return keys;
        }

        /// <summary>命中类骨架：放射裂纹，默认直径＝**卡宽 ×1.5**，再乘调用方给的
        /// 面积倍率（拉满出手 ×1.5；默认 ×1）。
        /// 碎块/尘雾均关闭——抛飞的大理石碎块俯视下像漂浮烟雾块（2026-07-26）。</summary>
        public static readonly ModeSpec ImpactMode =
            new ModeSpec(Mode.Impact, "ground_crack_hit", bakedLength: 2.0f, bakedWidth: 2.0f,
                         // GrowTime ≈ 非暴击 HitReact 窗（0.18）；与命中特效/抖动同拍张开
                         growthMode: 0f, growTime: 0.2f, oriented: false,
                         spurs: 0, cardWidthFactor: 1.5f, chunkCount: 0, dust: false);

        public static ModeSpec SpecOf(Mode mode) =>
            mode == Mode.Path ? PathMode : ImpactMode;
    }
}
