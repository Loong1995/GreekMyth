using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>雷霆神谕场域：画廊 2/8 · 11/61 ＝ Magic Effect19
    /// → <c>ambient_thunder_storm</c>（勿与命中件 <c>hit_lightning</c>＝同原料碰撞子件混淆）。
    ///
    /// 【为什么不是罩身】Effect19 的护罩壳按单人身位做，缩到一张卡上几乎看不见
    /// （壳是屏幕抓帧折射，低频舞台不可见，P-74/P-77）；真正有观感的是那层
    /// 世界空间游离电弧。故改判为**场域氛围件**：摘壳留电弧，源点钉主战场地面
    /// 中心铺满全场（`VfxUsage.AmbientField`）。挂载走 UnitAuraService 的
    /// `ambient_` 分流（全场按 key 去重）。状态 <c>thunder</c> 持有期间常显。</summary>
    public static class WireThunderStorm
    {
        const string Src =
            "Assets/KriptoFX/Magic Effects Pack v1/Prefabs/Effects/Effect19.prefab";
        const string Key = "ambient_thunder_storm";
        const string Dest = VfxPackStandardizer.VfxDir + "/" + Key + ".prefab";

        [InitializeOnLoadMethod]
        static void AutoHeal()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(Dest) != null) return;
                Debug.LogWarning("[ThunderStorm] ambient_thunder_storm 缺失，自动按 AmbientField 流水线补齐…");
                Wire();
            };
        }

        [MenuItem("GreekMyth/Magic Pack/接线雷霆神谕场域（画廊2/8·11/61 Effect19→ambient_thunder_storm）")]
        public static void Wire()
        {
            if (!VfxPackStandardizer.Standardize(Src, Key, VfxUsage.AmbientField))
            {
                Debug.LogError("[ThunderStorm] 落盘失败");
                return;
            }
            OrientAsStorm();
        }

        /// <summary>把电弧层从"护罩朝向"改成"自上而下劈"。
        ///
        /// 【为什么必须改】原料是绕人护罩：`LightningTrails` 的半球轴朝 +Y（往上窜），
        /// `LightningTrailsBottom` 的轴朝 +Z（**水平横喷**）。钉到地面中心当场域用时，
        /// 水平那层读出来就是"雷往镜头方向劈"——护罩的朝向语义在场域里不成立。
        /// 雷暴的方向语义只有一个：**从天上下来**。故两层统一轴朝 -Y 并抬到半空，
        /// 粒子从上方向下扎进地面。
        ///
        /// 【为什么写在接线脚本而不是流水线】朝向是**这件在这个用途下**的个性几何，
        /// 不是通用清洗；流水线只做与件无关的通用步骤（同罩身的个性裁层只进 Wire 名单）。</summary>
        static void OrientAsStorm()
        {
            var root = PrefabUtility.LoadPrefabContents(Dest);
            try
            {
                int n = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("LightningTrails", System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    // 绕 X 转 +90°：半球轴 +Z → -Y（朝下）
                    t.localEulerAngles = new Vector3(90f, 0f, 0f);
                    // 抬到半空：从地面高度朝下发射等于埋进地里，看不见落雷的行程
                    var p = t.localPosition;
                    t.localPosition = new Vector3(p.x, StrikeHeight, p.z);
                    n++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, Dest);
                Debug.Log($"[ThunderStorm] 电弧层改为自上而下（{n} 层，高度 {StrikeHeight}）");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>电弧起点相对源点的高度（件的局部单位，会再乘挂载期的场域尺度）。
        /// 太低看不出"从天上下来"，太高则落雷行程被拉长、显得慢。</summary>
        const float StrikeHeight = 2.5f;
    }
}
