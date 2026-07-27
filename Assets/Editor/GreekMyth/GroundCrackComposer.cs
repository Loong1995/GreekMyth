using System.Collections.Generic;
using System.IO;
using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // G4 裂地三层配方组合器（docs/client/ground_crack_language.md §3）。
    //
    // 产出 Resources/ClientBattle/VFX/ground_crack_{path,hit,arena}.prefab，
    // 每个都是同一套三层：
    //   L1 裂缝   平躺 Sprite 面片，遮罩镂空，色 = GroundCrackPalette.Crack
    //   L2 缝底   同遮罩缩小加深，色 = GroundCrackPalette.CrackCore
    //   L3 碎块   粒子，贴图 = 从舞台底图现切的 chunks_<stage> 图集（G3）
    // 三档之间只差「尺寸 / 遮罩形状 / 碎块量 / 存续」，材质与调色板同源
    // → 与任意底图构造上协调（换舞台重跑 G3 即自动跟色）。
    //
    // 遮罩自己烘而不直接把厂包贴图设成 Sprite：三张源图明暗极性不一致
    // （RFX4 Crack 白线黑底、Magic Crack1 黑线白底、CrackHeight 黑缝白底），
    // 直接用会出现「整块方块」或「全透明」。这里统一转成
    // RGB=白 + alpha=裂纹强度，颜色一律由调色板决定。
    // =========================================================================

    public static class GroundCrackComposer
    {
        const string OutDir = "Assets/Resources/ClientBattle/VFX";
        const string MaskDir = OutDir + "/masks";

        // 两类遮罩现在**全部自烘**：厂包 Crack1 带同心环裂纹，人工验收要求命中类
        // 只要「四散射线」，且环纹在放大后特别像贴图（2026-07-26）。见 BakeRadialMask。
        // 曾有第三张 arena 遮罩（CrackHeight 高度图）。2026-07-26 重组后场心大裂地
        // ＝命中类骨架 + 大面积 + 档 3，不再需要独立骨架，故不再烘制。

        /// <summary>碎块图集所用舞台（后续多舞台时按当前舞台切换）。</summary>
        const string ChunkStage = "olympus";

        /// <summary>弹道变体数：每套遮罩/prefab 固定哈希不同，出场随机抽，
        /// 避免「每次都是同一张两道大缝」（单遮罩必然复读）。</summary>
        public const int PathVariantCount = 4;

        [MenuItem("GreekMyth/裂地/G4 组合裂地骨架 Prefab（弹道/命中两类）")]
        public static void ComposeAll()
        {
            Directory.CreateDirectory(MaskDir);
            // 弹道：烘 PathVariantCount 套不同哈希的大小缝遮罩 + 同名 prefab
            for (int v = 0; v < PathVariantCount; v++)
            {
                var spine = BakePathMask($"mask_crack_spine_{v}", salt: v * 97 + 11);
                if (spine == null)
                {
                    Debug.LogError($"[CrackCompose] 弹道遮罩变体 {v} 烘制失败，中止");
                    return;
                }
                var pathMode = new GroundCrackPalette.ModeSpec(
                    GroundCrackPalette.Mode.Path, $"ground_crack_path_{v}",
                    GroundCrackPalette.PathMode.BakedLength,
                    GroundCrackPalette.PathMode.BakedWidth,
                    GroundCrackPalette.PathMode.GrowthMode,
                    GroundCrackPalette.PathMode.GrowTime,
                    GroundCrackPalette.PathMode.Oriented,
                    GroundCrackPalette.PathMode.Spurs,
                    GroundCrackPalette.PathMode.CardWidthFactor,
                    GroundCrackPalette.PathMode.ChunkCount,
                    GroundCrackPalette.PathMode.Dust);
                Compose(pathMode, spine, spurMask: null);
            }
            // 兼容旧 key：再存一份变体 0 为 ground_crack_path（探针/专配）
            var spine0 = AssetDatabase.LoadAssetAtPath<Sprite>(
                $"{MaskDir}/mask_crack_spine_0.png");
            Compose(GroundCrackPalette.PathMode, spine0, spurMask: null);

            var radial = BakeRadialMask("mask_crack_radial");
            AssetDatabase.Refresh();
            if (radial == null)
            {
                Debug.LogError("[CrackCompose] 命中遮罩烘制失败，中止");
                return;
            }
            Compose(GroundCrackPalette.ImpactMode, radial, spurMask: null);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CrackCompose] 弹道变体×{PathVariantCount} + 命中放射 已生成");
        }

        // ---------------------------------------------------------------- 遮罩
        //
        // 警告：Unity 的 Mathf.SmoothStep(from, to, t) 是「从 from 插到 to」，
        // **不是** HLSL smoothstep(edge0, edge1, x)！用错会把整条裂缝 alpha
        // 算成 ≤0，弹道裂地完全隐形（2026-07-26 实测 maxA=0）。

        /// <summary>HLSL 语义：x≤edge0→0，x≥edge1→1，中间 Hermite 平滑。</summary>
        static float EdgeSmooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>弹道遮罩（参考图语义）：**一条蜿蜒主缝**贯通全幅 + 树杈细枝网络。
        /// 主缝写进 R 通道（shader 用它门控熔岩：只有主缝烧），枝杈只有 alpha。</summary>
        static Sprite BakePathMask(string name, int salt)
        {
            const int w = 1024, h = 256;
            var mainSegs = new List<(Vector2, Vector2, float, float)>();
            var detailSegs = new List<(Vector2, Vector2, float, float)>();
            float midY = (h - 1) * 0.5f;

            // 单条蜿蜒主缝：起点靠左缘、贯穿到右缘，±40° 游走
            float startY = midY + (Hash01(salt, 3) - 0.5f) * midY * 0.6f;
            float rootW = Mathf.Lerp(7.5f, 11.5f, Hash01(salt, 30));
            GrowPathCrack(mainSegs, new Vector2(4f + Hash01(salt, 2) * 20f, startY),
                          w - 20f, rootW, midY, h, seed: salt * 17 + 3, bold: true,
                          dirSign: Hash01(salt, 4) < 0.5f ? -1f : 1f,
                          branchSegs: detailSegs);

            // 主缝周遭少量游离细缝（参考图里主缝旁的碎裂纹理），不带熔岩
            int microCount = 3 + Mathf.FloorToInt(Hash01(salt, 12) * 4f); // 3~6
            for (int i = 0; i < microCount; i++)
            {
                float y0 = midY + (Hash01(salt + i, 13) - 0.5f) * midY * 1.3f;
                float x0 = 12f + Hash01(salt + i, 14) * w * 0.85f;
                float len = w * Mathf.Lerp(0.03f, 0.1f, Hash01(salt + i, 15));
                float mw = Mathf.Lerp(1.2f, 2.6f, Hash01(salt + i, 16));
                GrowPathCrack(detailSegs, new Vector2(x0, y0), len, mw, midY, h,
                              salt * 40 + i * 13 + 400, bold: false, dirSign: 1f);
            }

            return WriteMaskSprite(name, w, h,
                                   RasterizeSegments(mainSegs, detailSegs, w, h));
        }

        /// <summary>弹道单条缝。主缝 ±40°；bold 时稀疏长出树杈分叉（从本体长出、长短不一）。
        /// branchSegs 非空时枝杈写进该表（细节层，无熔岩），主干仍写 segs。</summary>
        static Vector2 GrowPathCrack(List<(Vector2, Vector2, float, float)> segs, Vector2 start,
                                  float length, float rootW, float midY, float h, int seed,
                                  bool bold, float dirSign = 1f,
                                  List<(Vector2, Vector2, float, float)> branchSegs = null)
        {
            var branchSink = branchSegs ?? segs;
            int steps = Mathf.Max(4, Mathf.RoundToInt(length / (bold ? 24f : 28f)));
            float dx = length / steps;
            var cur = start;
            float dir = dirSign * (Hash01(seed, 11) - 0.5f) * (bold ? 1.12f : 0.55f);
            // 树杈预算随长度摊：全幅主缝约 6~9 根，别密成毛刷
            int branchBudget = bold
                ? Mathf.Max(2, Mathf.RoundToInt(length / 140f))
                  + Mathf.FloorToInt(Hash01(seed, 5) * 3f)
                : 0;
            int branchesLeft = branchBudget;

            for (int k = 0; k < steps; k++)
            {
                float t = k / (float)(steps - 1);
                float turn = bold ? 0.42f : 0.32f;
                dir += (Hash01(seed, 20 + k) - 0.5f) * turn;
                if (Hash01(seed, 80 + k) < (bold ? 0.28f : 0.14f))
                    dir += (Hash01(seed, 90 + k) < 0.5f ? -1f : 1f) *
                           Mathf.Lerp(bold ? 0.18f : 0.14f, bold ? 0.48f : 0.35f,
                                      Hash01(seed, 100 + k));
                float edge = bold ? 0.62f : 0.6f;
                float devi = (cur.y - midY) / midY;
                if (Mathf.Abs(devi) > edge)
                    dir -= Mathf.Sign(devi) * (Mathf.Abs(devi) - edge) * 1.3f;
                dir = Mathf.Clamp(dir, bold ? -0.70f : -0.55f, bold ? 0.70f : 0.55f);

                float stepX = dx * Mathf.Lerp(bold ? 0.78f : 0.9f, bold ? 1.22f : 1.1f,
                                              Hash01(seed, 110 + k));
                var next = new Vector2(cur.x + stepX, cur.y + stepX * Mathf.Tan(dir));
                next.y = Mathf.Clamp(next.y, 2f, h - 3f);
                if (next.x > 1020f) break;

                float env = bold
                    ? EdgeSmooth(0f, 0.04f, t) * EdgeSmooth(0f, 0.05f, 1f - t)
                    : EdgeSmooth(0f, 0.1f, t) * EdgeSmooth(0f, 0.12f, 1f - t);
                float wid = rootW * env * Mathf.Lerp(0.88f, 1.08f, Hash01(seed, 40 + k));
                segs.Add((cur, next, wid, wid * Mathf.Lerp(0.9f, 1.05f, Hash01(seed, 50 + k))));

                // 树杈：从主缝节点长出，稀疏、长短不一、左右不对称（示意图）
                if (bold && branchesLeft > 0 && k >= 1 && k <= steps - 2)
                {
                    // 把预算大致摊在前中后，再加随机，避免均匀梳齿
                    float due = (branchBudget - branchesLeft + 0.5f) / branchBudget;
                    bool nearSlot = Mathf.Abs(t - due) < 0.22f || Hash01(seed, 60 + k) < 0.08f;
                    if (nearSlot && Hash01(seed, 61 + k) < 0.55f)
                    {
                        SpawnTreeBranch(branchSink, next, dir, wid, h, seed + 800 + k * 3);
                        branchesLeft--;
                        // 偶发交叉成 X/Y（对侧再一根短的）
                        if (Hash01(seed, 88 + k) < 0.28f)
                            SpawnTreeBranch(branchSink, next, dir, wid * 0.85f, h,
                                            seed + 900 + k * 5,
                                            forceOpposite: true, shortBias: true);
                    }
                }

                cur = next;
            }
            return cur;
        }

        /// <summary>从主缝一点长出一根树杈：30°~60° 外撇，长度三档随机，缝宽可见但细于主缝。</summary>
        static void SpawnTreeBranch(List<(Vector2, Vector2, float, float)> segs, Vector2 from,
                                    float mainDir, float mainW, float h, int seed,
                                    bool forceOpposite = false, bool shortBias = false)
        {
            float side = forceOpposite
                ? (Hash01(seed, 1) < 0.5f ? -1f : 1f)
                : (Hash01(seed, 2) < 0.5f ? -1f : 1f);
            // 相对主缝 30°~60°（≈0.52~1.05 rad）
            float off = Mathf.Lerp(0.52f, 1.05f, Hash01(seed, 3));
            float ang = mainDir + side * off;
            // 长短不一：短 stubs / 中 / 长杈
            float roll = Hash01(seed, 4);
            float len;
            if (shortBias || roll < 0.35f) len = Mathf.Lerp(16f, 32f, Hash01(seed, 5));
            else if (roll < 0.7f) len = Mathf.Lerp(36f, 64f, Hash01(seed, 6));
            else len = Mathf.Lerp(70f, 110f, Hash01(seed, 7));

            float bW0 = Mathf.Clamp(mainW * Mathf.Lerp(0.55f, 0.85f, Hash01(seed, 8)), 2.0f, 4.5f);
            var tip = from + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;
            tip.y = Mathf.Clamp(tip.y, 2f, h - 3f);
            // 树杈本身再带一点折线感
            AddPolyline(segs, from, tip, bW0, bW0 * 0.4f, 3 + Mathf.FloorToInt(Hash01(seed, 9) * 2f),
                        0.35f, seed + 11);

            // 长杈偶发二级小杈（还是树，不是漂浮碎线）
            if (!shortBias && len > 55f && Hash01(seed, 10) < 0.4f)
            {
                var mid = Vector2.Lerp(from, tip, Mathf.Lerp(0.4f, 0.7f, Hash01(seed, 12)));
                float ang2 = ang + (Hash01(seed, 13) < 0.5f ? -1f : 1f) *
                             Mathf.Lerp(0.4f, 0.9f, Hash01(seed, 14));
                float len2 = Mathf.Lerp(12f, 34f, Hash01(seed, 15));
                var tip2 = mid + new Vector2(Mathf.Cos(ang2), Mathf.Sin(ang2)) * len2;
                tip2.y = Mathf.Clamp(tip2.y, 2f, h - 3f);
                AddPolyline(segs, mid, tip2, bW0 * 0.55f, bW0 * 0.25f, 3, 0.25f, seed + 20);
            }
        }

        /// <summary>把像素坐标下的线段表刷成 alpha 图（R 通道恒 1＝整图可燃）。</summary>
        static Color[] RasterizeSegments(List<(Vector2 a, Vector2 b, float w0, float w1)> segs,
                                         int w, int h)
            => RasterizeSegments(segs, null, w, h);

        /// <summary>主/细节双层刷图：alpha＝两层并集；R 通道只写主层覆盖度，
        /// shader 用 R 门控熔岩 → 只有主缝烧、枝杈保持暗（参考图语义）。</summary>
        static Color[] RasterizeSegments(List<(Vector2 a, Vector2 b, float w0, float w1)> mainSegs,
                                         List<(Vector2 a, Vector2 b, float w0, float w1)> detailSegs,
                                         int w, int h)
        {
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x, y);
                    float aMain = Coverage(mainSegs, p);
                    float aDetail = detailSegs == null ? 0f : Coverage(detailSegs, p);
                    float a = Mathf.Max(aMain, aDetail);
                    // 无细节层时 R 恒 1（命中遮罩整图可燃，行为不变）
                    float r = detailSegs == null ? 1f : Mathf.Clamp01(aMain);
                    pixels[y * w + x] = new Color(r, 1f, 1f, Mathf.Clamp01(a));
                }
            return pixels;
        }

        static float Coverage(List<(Vector2 a, Vector2 b, float w0, float w1)> segs, Vector2 p)
        {
            float a = 0f;
            foreach (var s in segs)
            {
                float pad = Mathf.Max(s.w0, s.w1) + 1f;
                if (p.x < Mathf.Min(s.a.x, s.b.x) - pad ||
                    p.x > Mathf.Max(s.a.x, s.b.x) + pad ||
                    p.y < Mathf.Min(s.a.y, s.b.y) - pad ||
                    p.y > Mathf.Max(s.a.y, s.b.y) + pad) continue;
                float t = SegT(p, s.a, s.b);
                float d = Vector2.Distance(p, Vector2.Lerp(s.a, s.b, t));
                float v = 1f - EdgeSmooth(Mathf.Lerp(s.w0, s.w1, t) * 0.4f,
                                          Mathf.Lerp(s.w0, s.w1, t), d);
                if (v > a) a = v;
            }
            return a;
        }

        static Sprite WriteMaskSprite(string name, int w, int h, Color[] pixels)
        {
            // 验收：写出前必须有实心像素，否则弹道裂地会「完全看不见」
            float maxA = 0f;
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > maxA) maxA = pixels[i].a;
            if (maxA < 0.5f)
            {
                Debug.LogError($"[CrackCompose] {name} 烘出 maxA={maxA:F3}，拒绝写入（会隐形）");
                return null;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            string dest = $"{MaskDir}/{name}.png";
            File.WriteAllBytes(dest, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(dest) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.sRGBTexture = false; // 遮罩当数据，别被 sRGB 啃 alpha
                importer.SaveAndReimport();
            }
            Debug.Log($"[CrackCompose] {dest} ← procedural {w}×{h} maxA={maxA:F2}");
            return AssetDatabase.LoadAssetAtPath<Sprite>(dest);
        }

        /// <summary>命中主遮罩：**递归分叉的砸裂纹**，无同心环。
        ///
        /// 现实里砸裂石板不是几根等长直线（那读作"敷衍的星形"，2026-07-26 打回），
        /// 而是：主缝从冲击点放射 → 沿途**逐级分叉**且子缝更细更短 → 缝宽沿程起伏
        /// → 中心区被密集短缝打碎 → 相邻主缝之间偶有**短连接缝**把碎块勾出来。
        /// 唯一禁止项仍是完整同心环（一加就读作贴图）。
        /// 烘制必须可复现：随机数一律走固定哈希，禁止 Random。</summary>
        static Sprite BakeRadialMask(string name)
        {
            const int size = 512;
            const int spokes = 10;  // 主缝少而有主次，细节交给分叉
            var segs = new List<(Vector2 a, Vector2 b, float w0, float w1)>();
            var mainDirs = new List<(float ang, float len)>();

            for (int i = 0; i < spokes; i++)
            {
                // 角度：均分位 ±0.55rad，长度差距拉到 3 倍以上，并且**不从正中心起**
                // ——所有缝交于一点＝一眼看穿的"四射星"（2026-07-26 二次打回）。
                // 抖动 ±0.3rad：放到 ±0.55 时实测整片缝聚到一侧，另一半是空地
                float ang = i / (float)spokes * Mathf.PI * 2f + (Hash01(i, 3) - 0.5f) * 0.6f;
                float len = Mathf.Lerp(0.42f, 1.0f, Hash01(i, 11));
                float rootW = Mathf.Lerp(0.022f, 0.055f, Hash01(i, 17));
                float off = Mathf.Lerp(0f, 0.09f, Hash01(i, 5));
                float offAng = Hash01(i, 9) * Mathf.PI * 2f;
                var start = new Vector2(Mathf.Cos(offAng), Mathf.Sin(offAng)) * off;
                mainDirs.Add((ang, len));
                GrowCrack(segs, start, ang + (Hash01(i, 15) - 0.5f) * 0.4f,
                          len, rootW, depth: 0, seed: i * 7 + 1);
            }

            // 次级裂源：离心几处独立小裂网。真实砸击会在薄弱处二次开裂，
            // 有了它整张图就不是"一个中心 + N 条腿"。
            for (int i = 0; i < 4; i++)
            {
                float ang = Hash01(i, 121) * Mathf.PI * 2f;
                float r = Mathf.Lerp(0.22f, 0.55f, Hash01(i, 127));
                var hub = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                int arms = 2 + Mathf.FloorToInt(Hash01(i, 131) * 3f);
                for (int j = 0; j < arms; j++)
                    GrowCrack(segs, hub,
                              Hash01(i * 13 + j, 137) * Mathf.PI * 2f,
                              Mathf.Lerp(0.12f, 0.38f, Hash01(i * 13 + j, 139)),
                              Mathf.Lerp(0.014f, 0.028f, Hash01(i * 13 + j, 149)),
                              depth: 1, seed: 700 + i * 13 + j);
            }

            // 中心破碎区：一圈很短的细缝，让冲击点是"砸碎"而不是"线汇聚"
            for (int i = 0; i < 12; i++)
            {
                float ang = Hash01(i, 53) * Mathf.PI * 2f;
                float r0 = Mathf.Lerp(0.02f, 0.14f, Hash01(i, 59));
                var from = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r0;
                GrowCrack(segs, from, ang + (Hash01(i, 61) - 0.5f) * 1.6f,
                          Mathf.Lerp(0.05f, 0.18f, Hash01(i, 67)), 0.017f, depth: 2,
                          seed: 900 + i);
            }

            // 短连接缝：勾在相邻两条主缝之间的一小段（碎块的边），**不成环**
            for (int i = 0; i < mainDirs.Count; i++)
            {
                if (Hash01(i, 71) < 0.45f) continue;
                var m0 = mainDirs[i];
                var m1 = mainDirs[(i + 1) % mainDirs.Count];
                float r = Mathf.Lerp(0.25f, 0.62f, Hash01(i, 73));
                var a = new Vector2(Mathf.Cos(m0.ang), Mathf.Sin(m0.ang)) * (r * m0.len);
                var b = new Vector2(Mathf.Cos(m1.ang), Mathf.Sin(m1.ang)) *
                        (r * m1.len * Mathf.Lerp(0.85f, 1.15f, Hash01(i, 79)));
                // 只连一段（0.55~0.8），留缺口，避免拼成闭合环
                var end = Vector2.Lerp(a, b, Mathf.Lerp(0.55f, 0.8f, Hash01(i, 83)));
                AddPolyline(segs, a, end, 0.012f, 0.005f, 3, 0.18f, 500 + i);
            }

            var pixels = new Color[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 归一化到 [-1,1]，遮罩长轴＝直径
                    var p = new Vector2((x - half) / half, (y - half) / half);
                    float a = 0f;
                    foreach (var s in segs)
                    {
                        // 包围盒快筛：段数已到数百，逐段算距离会让烘制卡十几秒
                        float pad = Mathf.Max(s.w0, s.w1) + 0.01f;
                        if (p.x < Mathf.Min(s.a.x, s.b.x) - pad ||
                            p.x > Mathf.Max(s.a.x, s.b.x) + pad ||
                            p.y < Mathf.Min(s.a.y, s.b.y) - pad ||
                            p.y > Mathf.Max(s.a.y, s.b.y) + pad) continue;
                        float t = SegT(p, s.a, s.b);
                        float d = Vector2.Distance(p, Vector2.Lerp(s.a, s.b, t));
                        float w = Mathf.Lerp(s.w0, s.w1, t);
                        float v = 1f - EdgeSmooth(w * 0.35f, w, d);
                        if (v > a) a = v;
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                }
            return WriteMaskSprite(name, size, size, pixels);
        }

        /// <summary>一条裂缝的生长：走 N 步，每步转个小角、收细一点，
        /// 途中按概率**分叉**出更细更短的子缝（递归到 depth 上限）。
        /// 这层递归才是「真实砸裂」与「星形图标」的分界。</summary>
        static void GrowCrack(List<(Vector2, Vector2, float, float)> segs, Vector2 origin,
                              float ang, float len, float rootW, int depth, int seed)
        {
            const int maxDepth = 3;
            int steps = depth == 0 ? 10 : 5; // 段越短越不像直线
            var cur = origin;
            float curAng = ang;
            float curW = rootW;
            float step = len / steps;

            for (int k = 0; k < steps; k++)
            {
                float f = (k + 1) / (float)steps;
                // 走向：小幅折转（越远越野），模拟沿晶界扩展
                // 恒定弯曲 + 随机折转：只有随机折转时，缝整体仍沿着起始方向"辐射"，
                // 加一点固定弧度后每条缝会各自拐弯，径向感被打断
                float curl = (Hash01(seed, 353) - 0.5f) * 0.34f;
                curAng += curl + (Hash01(seed, 101 + k) - 0.5f) * (0.32f + 0.35f * f);
                var next = cur + new Vector2(Mathf.Cos(curAng), Mathf.Sin(curAng)) *
                                 step * Mathf.Lerp(0.75f, 1.25f, Hash01(seed, 131 + k));
                // 缝宽沿程收细 + 起伏，等宽线看着像画笔而不像裂开
                float nextW = rootW * Mathf.Lerp(1f, 0.12f, f) *
                              Mathf.Lerp(0.75f, 1.3f, Hash01(seed, 151 + k));
                segs.Add((cur, next, curW, nextW));

                // 分叉：越靠根部越容易分，子缝继承当前宽度的一半
                if (depth < maxDepth && k >= 1 &&
                    Hash01(seed, 181 + k) < (depth == 0 ? 0.55f : 0.3f))
                {
                    float side = Hash01(seed, 211 + k) < 0.5f ? 1f : -1f;
                    GrowCrack(segs, next,
                              curAng + side * Mathf.Lerp(0.35f, 0.9f, Hash01(seed, 241 + k)),
                              len * (1f - f) * Mathf.Lerp(0.4f, 0.8f, Hash01(seed, 271 + k)),
                              nextW * Mathf.Lerp(0.5f, 0.8f, Hash01(seed, 307 + k)),
                              depth + 1, seed * 31 + k + 13);
                }
                cur = next; curW = nextW;
            }
        }

        /// <summary>两点之间的抖折折线（用于短连接缝）。</summary>
        static void AddPolyline(List<(Vector2, Vector2, float, float)> segs, Vector2 a, Vector2 b,
                                float w0, float w1, int parts, float jitter, int seed)
        {
            Vector2 dir = (b - a).normalized;
            var n = new Vector2(-dir.y, dir.x);
            var prev = a;
            for (int k = 1; k <= parts; k++)
            {
                float f = k / (float)parts;
                var p = Vector2.Lerp(a, b, f) +
                        n * (Hash01(seed, 331 + k) - 0.5f) * jitter * (b - a).magnitude *
                        (k == parts ? 0f : 1f);
                segs.Add((prev, p, Mathf.Lerp(w0, w1, (k - 1) / (float)parts),
                          Mathf.Lerp(w0, w1, f)));
                prev = p;
            }
        }

        /// <summary>点到线段的投影参数（0..1）。</summary>
        static float SegT(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = Vector2.Dot(ab, ab);
            if (len2 < 1e-8f) return 0f;
            return Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        }

        /// <summary>固定哈希伪随机（0..1）。烘制必须可复现，禁止用 Random。</summary>
        static float Hash01(int a, int b)
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        // ---------------------------------------------------------------- 组装

        static void Compose(GroundCrackPalette.ModeSpec mode, Sprite mask, Sprite spurMask)
        {
            var root = new GameObject(mode.Key);
            root.AddComponent<VfxGroundLayer>(); // 排序豁免：地面层不得被抬到卡牌之上

            if (mask != null) BuildCrackGroup(root.transform, mode, mask, spurMask);
            if (mode.ChunkCount > 0) BuildChunks(root.transform, mode, mode.ChunkCount);
            if (mode.Dust) BuildDust(root.transform, mode);

            string dest = $"{OutDir}/{mode.Key}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, dest);
            Object.DestroyImmediate(root);
            Debug.Log($"[CrackCompose] {dest} 骨架=L1+L2" +
                      $"{(mode.Spurs > 0 ? $"+毛刺×{mode.Spurs}" : "")}" +
                      $"{(mode.Dust ? "+尘雾" : "")}" +
                      $"{(mode.ChunkCount > 0 ? $"+碎块×{mode.ChunkCount}" : "")}");
        }

        /// <summary>L1 裂缝 + L2 缝底（+ 弹道类的毛刺）：同一遮罩，
        /// 组节点平躺并承载淡入淡出。
        ///
        /// 这里只烘**骨架**：缝宽/持续/亮度属强度档，面积属调用参数，
        /// 都由 `GroundCrackService` 在出场时写入（同一件三档通吃）。
        /// prefab 里先写档 1 作为「没人写也能看」的兜底。</summary>
        static void BuildCrackGroup(Transform parent, GroundCrackPalette.ModeSpec mode,
                                    Sprite mask, Sprite spurMask)
        {
            var group = new GameObject("CrackGroup");
            group.transform.SetParent(parent, false);
            group.transform.localPosition = new Vector3(0f, GroundCrackPalette.LiftY, 0f);
            // 先关掉再挂组件：AddComponent 会立刻 OnEnable；若此时 RandomizeSpin
            // 仍是默认 true，俯仰 90° 读改 euler 会把错误朝向烤进 prefab（弹道档
            // 看起来像几道互不相关的独立裂地）。
            group.SetActive(false);
            var decal = group.AddComponent<GroundCrackDecal>();
            decal.FadeIn = GroundCrackPalette.FadeIn;
            decal.RandomizeSpin = !mode.Oriented;
            decal.BakedSize = mode.BakedWidth;
            decal.CardWidthFactor = mode.CardWidthFactor;
            decal.GrowTime = mode.GrowTime;
            decal.GrowthMode = mode.GrowthMode;
            decal.ApplyStrength(GroundCrackPalette.Strength.Light); // 兜底档
            // 绕 X 转 90° 平躺（与 ArenaGround 一致）；弹道朝向由运行时根节点 yaw 给
            group.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            AddCrackQuad(group.transform, "Crack", mask, mode,
                         GroundCrackPalette.Crack, GroundCrackPalette.SortingOrder, 1f);
            // 缝底比裂缝窄，露在裂缝内侧形成「缝里有深处」的层次
            AddCrackQuad(group.transform, "CrackCore", mask, mode,
                         GroundCrackPalette.CrackCore, GroundCrackPalette.SortingOrder + 1, 0.55f);
            BuildSpurs(group.transform, mode, spurMask ?? mask);
            group.SetActive(true);
            // 激活会跑 OnEnable（命中档随机自旋 + Apply(0) 把面片 alpha 清零）；
            // prefab 里必须保留干净平躺基准与**满 alpha 基色**——alpha=0 一旦烤进
            // 资产，运行期 Collect 会把它当基色缓存，裂地永远全透明（2026-07-26）。
            group.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (var sr in group.GetComponentsInChildren<SpriteRenderer>(true))
                sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f);
        }

        /// <summary>毛刺：主缝两侧浅角短细枝（主方向仍是那一道）。
        /// 浅角 18~28°、长仅主缝 20%~28%，根贴主脊；写死布局不随机。
        /// 用独立短枝遮罩，禁止复用主缝锯齿图（会杂乱）。</summary>
        static void BuildSpurs(Transform group, GroundCrackPalette.ModeSpec mode, Sprite spurMask)
        {
            if (mode.Spurs <= 0 || spurMask == null) return;

            // (沿主缝 t∈[-0.5,0.5]，侧向 ±1，夹角°，长度占主缝比例)
            var layout = new[]
            {
                new Vector4(-0.28f,  1f, 22f, 0.24f),
                new Vector4(-0.06f, -1f, 26f, 0.28f),
                new Vector4( 0.16f,  1f, 20f, 0.22f),
                new Vector4( 0.34f, -1f, 24f, 0.20f),
            };

            var size = spurMask.bounds.size;
            float spurWidth = mode.BakedWidth * 0.45f;
            for (int i = 0; i < mode.Spurs && i < layout.Length; i++)
            {
                float t = layout[i].x, side = layout[i].y;
                float angle = layout[i].z * side, lengthRatio = layout[i].w;
                float length = mode.BakedLength * lengthRatio;

                var go = new GameObject($"Spur{i + 1}");
                go.transform.SetParent(group, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = spurMask;
                sr.color = GroundCrackPalette.Crack;
                sr.sortingOrder = GroundCrackPalette.SortingOrder;
                sr.sharedMaterial = EnsureCrackMaterial();

                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                float rootOut = side * mode.BakedWidth * 0.15f;
                float tipOut = length * 0.45f * Mathf.Sin(Mathf.Abs(angle) * Mathf.Deg2Rad);
                go.transform.localPosition = new Vector3(
                    mode.BakedLength * t,
                    rootOut + side * tipOut,
                    0f);
                go.transform.localScale = new Vector3(
                    length / Mathf.Max(0.0001f, size.x),
                    spurWidth / Mathf.Max(0.0001f, size.y),
                    1f);
            }
        }

        static void AddCrackQuad(Transform parent, string name, Sprite mask,
                                 GroundCrackPalette.ModeSpec mode, Color color,
                                 int order, float shrink)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = mask;
            sr.color = color;
            sr.sortingOrder = order;
            sr.sharedMaterial = EnsureCrackMaterial();

            // 遮罩图长宽比与骨架目标尺寸无关，两轴各自拉到位（弹道类靠这个拉长）
            var size = mask.bounds.size;
            go.transform.localScale = new Vector3(
                mode.BakedLength / Mathf.Max(0.0001f, size.x) * shrink,
                mode.BakedWidth / Mathf.Max(0.0001f, size.y) * shrink,
                1f);
        }

        /// <summary>裂缝材质：自研 GroundCrack shader（生长 + 熔岩锋面）。
        /// 三档共用一份，档位差异全部走 MaterialPropertyBlock 由 GroundCrackDecal
        /// 运行期写入，避免为分档裂出一堆材质变体。</summary>
        static Material EnsureCrackMaterial()
        {
            const string path = OutDir + "/mat_ground_crack.mat";
            var shader = Shader.Find("GreekMyth/GroundCrack");
            if (shader == null)
            {
                Debug.LogError("[CrackCompose] 找不到 GreekMyth/GroundCrack shader");
                return null;
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_GlowColor", GroundCrackPalette.Lava);
            mat.SetFloat("_FrontWidth", 0.10f);
            mat.SetFloat("_Softness", 0.05f);
            mat.SetFloat("_EmberFloor", 0.07f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>L3 碎块：贴图取 G3 从底图现切的图集，Texture Sheet Animation
        /// 随机定帧 → 每颗粒子是一块不同的「本地石头」。</summary>
        static void BuildChunks(Transform parent, GroundCrackPalette.ModeSpec mode, int count)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>($"{OutDir}/chunks_{ChunkStage}.png");
            if (atlas == null)
            {
                Debug.LogError($"[CrackCompose] 缺碎块图集 chunks_{ChunkStage}.png（先跑 G3）");
                return;
            }

            var go = new GameObject("Chunks");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.30f, 0.62f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 2.6f; // 抛飞后要落回地面，不能飘
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            main.startColor = Color.white; // 颜色全靠贴图（＝底图本身），禁止在此染色

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count))
            });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 42f;
            shape.radius = Mathf.Max(0.15f, mode.BakedWidth * 0.35f);
            shape.rotation = new Vector3(-90f, 0f, 0f); // 锥口朝天（默认朝 +z）

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-3f, 3f); // 弧度/秒，正负＝随机翻滚方向
            rot.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 4;
            tsa.numTilesY = 3;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 12f); // 每颗随机取一块
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);   // 定帧，不播动画

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = EnsureChunkMaterial(atlas);
            // 抛飞的石头允许压在卡面之前（石头是从地上崩起来的，挡一点卡是对的）
            renderer.sortingOrder = 46;
        }

        /// <summary>尘雾辅助层：软圆贴图 + 调色板 Dust 染色。
        /// 首版用无贴图白材质，粒子 billboard 渲成方块（雅典娜受击时肉眼可见），
        /// 2026-07-26 改为自烘径向衰减圆，边缘柔和、没有方角。</summary>
        static void BuildDust(Transform parent, GroundCrackPalette.ModeSpec mode)
        {
            var go = new GameObject("Dust");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.duration = 0.8f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(mode.BakedWidth * 0.25f, mode.BakedWidth * 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(GroundCrackPalette.Dust.r, GroundCrackPalette.Dust.g,
                          GroundCrackPalette.Dust.b, 0.35f));
            main.gravityModifier = -0.15f; // 微微上飘
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = mode.BakedWidth * 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                        new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(grad);

            // 生命周期里略胀再收，避免硬切消失
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.15f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // 贴地水平 billboard：俯视舞台下，立着的竖屏粒子即使有软圆贴图
            // 也会被读成「一块块立牌」；平躺后才是地面扬尘。
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.sharedMaterial = EnsureDustMaterial();
            renderer.sortingOrder = GroundCrackPalette.DustSortingOrder;
        }

        /// <summary>尘雾材质：自烘软圆贴图。用 URP/Unlit（不用 Particles/Unlit）——
        /// Particles/Unlit 在部分 URP 版本上不把贴图 alpha 乘进最终透明度，
        /// 结果是一整块方角 billboard（雅典娜受击时的「方块烟雾」）。
        /// 每次 G4 都把贴图写回，避免旧版无贴图材质被缓存沿用。</summary>
        static Material EnsureDustMaterial()
        {
            var tex = BakeSoftCircle("tex_ground_dust");
            string path = $"{OutDir}/mat_ground_dust.mat";
            // 旧材质若挂的是 Particles/Unlit，直接换 shader（同资产覆盖，池化实例同步）
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
                mat.shader = shader;

            mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);     // Alpha
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);       // 双面，俯视不丢
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>径向衰减软圆（中心白、边缘透明）。粒子无贴图时 billboard
        /// 是方角四边形，这张图把边角压成 0 alpha，读作烟团。</summary>
        static Texture2D BakeSoftCircle(string name)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float mid = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - mid) / mid, dy = (y - mid) / mid;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // 更狠的衰减：r∈[0.2,0.95] 就落到 0，角上绝不留残影
                float a = 1f - Mathf.SmoothStep(0.2f, 0.95f, r);
                a *= a; // 再压一档，边缘更柔
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply(false, false);

            Directory.CreateDirectory(OutDir);
            string path = $"{OutDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            // DXT5 会啃软 alpha 边缘成方角块；尘雾贴图很小，不解压也无妨
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>碎块粒子材质：URP 透明无光照 + 底图现切图集。</summary>
        static Material EnsureChunkMaterial(Texture2D atlas)
        {
            string path = $"{OutDir}/mat_ground_chunk.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (atlas != null)
            {
                mat.mainTexture = atlas;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
            }
            // Transparent + Alpha 混合（碎块有硬边 alpha，不能用 additive 否则发光）
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
