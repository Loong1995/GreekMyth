using System.IO;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // G3 碎块现切（docs/client/ground_crack_language.md §3.1）。
    //
    // 从舞台地面底图 Resources/ClientBattle/Arena/arena_<stage>.png 的**竞技区
    // 中央**随机取 12 个方块，各乘一张随机凸多边形 alpha 遮罩、断口压暗，
    // 拼成 4×3 图集 Resources/ClientBattle/VFX/chunks_<stage>.png。
    //
    // 为什么这么做：碎块颜色因此天生等于地面本身 → 三档裂地与任意底图构造上
    // 协调，换舞台（神/人/妖）只要重跑本工具，零美术工作量。
    //
    // 底图读取走 File.ReadAllBytes + LoadImage，而非 AssetDatabase 取 Texture2D：
    // 后者受 importer 的 isReadable/压缩格式限制（Sprite 默认不可读，GetPixels 抛错），
    // 直接读 PNG 字节可完全绕开 importer 状态。
    // =========================================================================

    public static class GroundChunkBaker
    {
        const string ArenaDir = "Assets/Resources/ClientBattle/Arena";
        const string OutDir = "Assets/Resources/ClientBattle/VFX";

        const int ChunkSize = 96;
        const int Cols = 4;
        const int Rows = 3;

        /// <summary>只在底图中央这块归一化矩形里取样，避开观众席/看台纹理。</summary>
        static readonly Rect SampleRegion = new Rect(0.32f, 0.34f, 0.36f, 0.30f);

        [MenuItem("GreekMyth/裂地/G3 烘碎块图集（全部舞台）")]
        public static void BakeAll()
        {
            if (!Directory.Exists(ArenaDir))
            {
                Debug.LogError("[ChunkBaker] 缺目录 " + ArenaDir);
                return;
            }
            int done = 0;
            foreach (var file in Directory.GetFiles(ArenaDir, "arena_*.png"))
            {
                var stage = Path.GetFileNameWithoutExtension(file).Substring("arena_".Length);
                if (Bake(stage)) done++;
            }
            AssetDatabase.Refresh();
            Debug.Log($"[ChunkBaker] 完成 {done} 个舞台");
        }

        /// <summary>烘一个舞台的碎块图集；返回是否成功。</summary>
        public static bool Bake(string stage)
        {
            string src = $"{ArenaDir}/arena_{stage}.png";
            if (!File.Exists(src))
            {
                Debug.LogError("[ChunkBaker] 缺底图 " + src);
                return false;
            }

            var ground = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ground.LoadImage(File.ReadAllBytes(src)))
            {
                Debug.LogError("[ChunkBaker] 底图解码失败 " + src);
                return false;
            }

            // 固定种子：同一底图每次烘出同一套碎块，便于回归比对
            var rng = new System.Random(StableSeed(stage));

            var atlas = new Texture2D(ChunkSize * Cols, ChunkSize * Rows, TextureFormat.RGBA32, false);
            var clear = new Color32[atlas.width * atlas.height];
            atlas.SetPixels32(clear);

            int x0 = Mathf.RoundToInt(SampleRegion.xMin * ground.width);
            int y0 = Mathf.RoundToInt(SampleRegion.yMin * ground.height);
            int spanX = Mathf.Max(1, Mathf.RoundToInt(SampleRegion.width * ground.width) - ChunkSize);
            int spanY = Mathf.Max(1, Mathf.RoundToInt(SampleRegion.height * ground.height) - ChunkSize);

            for (int idx = 0; idx < Cols * Rows; idx++)
            {
                int sx = x0 + rng.Next(spanX);
                int sy = y0 + rng.Next(spanY);
                sx = Mathf.Clamp(sx, 0, ground.width - ChunkSize);
                sy = Mathf.Clamp(sy, 0, ground.height - ChunkSize);

                var pixels = ground.GetPixels(sx, sy, ChunkSize, ChunkSize);
                ApplyChunkMask(pixels, rng);

                atlas.SetPixels(idx % Cols * ChunkSize, idx / Cols * ChunkSize,
                                ChunkSize, ChunkSize, pixels);
            }
            atlas.Apply();

            Directory.CreateDirectory(OutDir);
            string dest = $"{OutDir}/chunks_{stage}.png";
            File.WriteAllBytes(dest, atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
            ConfigureAtlasImporter(dest);

            Object.DestroyImmediate(ground);
            Object.DestroyImmediate(atlas);
            Debug.Log($"[ChunkBaker] {dest} ← arena_{stage}.png 中央区 {Cols}×{Rows} 块");
            return true;
        }

        /// <summary>把方块切成不规则石片：随机凸多边形镂空 + 断口边缘压暗。</summary>
        static void ApplyChunkMask(Color[] pixels, System.Random rng)
        {
            int verts = 4 + rng.Next(4); // 4~7 边
            var poly = new Vector2[verts];
            float center = ChunkSize * 0.5f;
            for (int i = 0; i < verts; i++)
            {
                // 顶点按角度均分再加抖动 → 保证不自交（星形凸包近似）
                float angle = (i + (float)rng.NextDouble() * 0.6f - 0.3f) / verts * Mathf.PI * 2f;
                float radius = center * (0.62f + (float)rng.NextDouble() * 0.34f);
                poly[i] = new Vector2(center + Mathf.Cos(angle) * radius,
                                      center + Mathf.Sin(angle) * radius);
            }

            var inside = new bool[ChunkSize * ChunkSize];
            for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                    inside[y * ChunkSize + x] = InPolygon(poly, x + 0.5f, y + 0.5f);

            for (int y = 0; y < ChunkSize; y++)
                for (int x = 0; x < ChunkSize; x++)
                {
                    int i = y * ChunkSize + x;
                    if (!inside[i]) { pixels[i] = new Color(0f, 0f, 0f, 0f); continue; }
                    var c = pixels[i];
                    c.a = 1f;
                    if (IsBorder(inside, x, y)) // 断口：新崩开的截面比表面暗
                    {
                        c.r *= 0.5f; c.g *= 0.5f; c.b *= 0.5f;
                    }
                    pixels[i] = c;
                }
        }

        /// <summary>2px 内有外部像素即视为断口边缘。</summary>
        static bool IsBorder(bool[] inside, int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= ChunkSize || ny >= ChunkSize) return true;
                    if (!inside[ny * ChunkSize + nx]) return true;
                }
            return false;
        }

        /// <summary>射线穿越法点在多边形内判定。</summary>
        static bool InPolygon(Vector2[] poly, float px, float py)
        {
            bool hit = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (poly[i].y > py != poly[j].y > py &&
                    px < (poly[j].x - poly[i].x) * (py - poly[i].y) /
                         (poly[j].y - poly[i].y) + poly[i].x)
                    hit = !hit;
            }
            return hit;
        }

        /// <summary>图集给粒子材质 + Texture Sheet Animation 用：Default 类型、
        /// 不压缩（保住断口细节）、无 mipmap（避免远处糊成一团）。</summary>
        static void ConfigureAtlasImporter(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>舞台名 → 稳定种子（不用 string.GetHashCode，它跨运行不保证一致）。</summary>
        static int StableSeed(string stage)
        {
            int h = 17;
            foreach (char c in stage) h = h * 31 + c;
            return h;
        }
    }
}
