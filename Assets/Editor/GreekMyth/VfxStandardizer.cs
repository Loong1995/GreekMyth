using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClientBattle.Units;
using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // 全量 VFX prefab 标准化（docs/client/vfx_pack_integration.md §三/§四）。
    //
    // 做两件事，都可重复执行且幂等：
    // 1) 尺寸归一：非地面件补挂 VfxFitter，基准记为「设计布局下的卡宽」。
    //    首次写入取 Factor=1（观感与现状完全一致），此后调这一个数即可；
    //    布局/机型变化时特效相对卡牌的占比自动保持。
    // 2) Built-in 残留清理：删掉 URP 下无效的 Projector 等组件。
    //
    // 为什么用工具而不是手工挂：52 个 prefab 手工挂必漏，且漏了不会报错，
    // 只会在某个机型上尺寸不对——这类问题事后极难定位。
    // =========================================================================

    public static class VfxStandardizer
    {
        const string VfxDir = "Assets/Resources/ClientBattle/VFX";

        /// <summary>尺寸归一的参照卡宽。单体制（矩形六等分格）下由
        /// StanceLayout.Recalc 得出；与画廊/战斗同源，避免双档卡宽踩坑（P-38）。
        /// 地面板动态化后卡尺只由格纵深决定、与宽高比无关，设计基准锁 16:9。</summary>
        static float ReferenceCardWidth
        {
            get
            {
                float savedRot = BattlefieldLayout.RotationDeg;
                BattlefieldLayout.RotationDeg = 0f; // 设计基准锁定不旋转
                BattlefieldLayout.Recalc(BattlefieldLayout.DesignAspect);
                StanceLayout.Recalc(StanceLayout.DesignHalfWidth, StanceLayout.DesignHalfHeight);
                float w = StanceLayout.CardWidth;
                BattlefieldLayout.RotationDeg = savedRot;
                return w;
            }
        }

        [MenuItem("GreekMyth/特效/标准化 尺寸归一 + 清理残留")]
        public static void StandardizeAll()
        {
            float designBasis = ReferenceCardWidth;
            var log = new StringBuilder();
            int fitted = 0, cleaned = 0, skipped = 0, rebased = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxDir })
                         .OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;

                foreach (var stale in CollectBuiltinLeftovers(root))
                {
                    log.AppendLine($"  清理 {stale.GetType().Name} @ {root.name}");
                    Object.DestroyImmediate(stale, true);
                    cleaned++;
                    dirty = true;
                }

                foreach (var dead in CollectDeadDecals(root))
                {
                    log.AppendLine($"  清理 死贴花节点 {dead.name} @ {root.name}");
                    Object.DestroyImmediate(dead, true);
                    cleaned++;
                    dirty = true;
                }

                if (root.GetComponent<VfxGroundLayer>() != null
                    || root.name.StartsWith("shroud_"))
                {
                    skipped++; // 地面件 / 罩身件尺寸自管（ShroudFitter）
                }
                else
                {
                    var fitter = root.GetComponentInChildren<VfxFitter>(true);
                    if (fitter == null)
                    {
                        fitter = root.AddComponent<VfxFitter>();
                        fitter.Reference = VfxFitter.Basis.CardWidth;
                        fitter.Factor = 1f;
                        fitted++;
                        dirty = true;
                    }
                    // 参照值改了要回填，否则老件继续按旧基准缩放
                    if (!Mathf.Approximately(fitter.BakedBasis, designBasis))
                    {
                        fitter.BakedBasis = designBasis;
                        rebased++;
                        dirty = true;
                    }
                }

                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[VfxStd] 参照卡宽={designBasis:F3}；补挂 VfxFitter {fitted} 件、" +
                      $"回填基准 {rebased} 件、清理 Built-in 残留 {cleaned} 处、" +
                      $"地面件跳过 {skipped} 件\n{log}");
        }

        /// <summary>URP 下无效的 Built-in 专属组件。Projector 在 Unity 6 已弃用，
        /// 留着不会报错，只会静默不出图（P-33 同族问题）。</summary>
        static List<Component> CollectBuiltinLeftovers(GameObject root)
        {
            var stale = new List<Component>();
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c is Projector) stale.Add(c);
            }
            return stale;
        }

        /// <summary>厂包深度投影贴花：shader 能编译、URP 下却画不出任何东西（实测见 P-33）。
        /// 留着只是白付一次 draw call，直接摘掉节点；裂地统一走自研三层配方。</summary>
        static List<GameObject> CollectDeadDecals(GameObject root)
        {
            var dead = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                if (shader.name != "KriptoFX/RFX1/Decal" && shader.name != "KriptoFX/RFX4/Decal") continue;
                if (r.gameObject == root) continue;
                dead.Add(r.gameObject);
            }
            return dead;
        }
    }
}
