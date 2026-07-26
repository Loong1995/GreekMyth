using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    // =========================================================================
    // G11 裂地静态探针（docs/client/ground_crack_language.md §四.五）。
    //
    // 为什么需要：裂地是 0.8~2.6 秒的瞬时演出，靠"打一场再截图"命中率极低
    // （P-34 验收教训）。本工具在 Play 中把 2 模式 ×3 档一次全摆到地面并延长
    // 存续，用于分辨「没接线」和「接了但渲不出来」这两类完全不同的故障，
    // 同时能一眼比出三档台阶是否真的拉开了差距。
    //
    // 只做诊断，不参与正式演出；Play 停止即随实例消失。
    // =========================================================================

    public static class GroundCrackProbe
    {
        [MenuItem("GreekMyth/裂地/G11 静态探针（2 模式 ×3 档摆到地面）")]
        public static void Spawn()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[CrackProbe] 需在 Play 模式下用（依赖运行期卡宽与地面）");
                return;
            }
            var vfx = Object.FindAnyObjectByType<VFXManager>();
            if (vfx == null)
            {
                Debug.LogError("[CrackProbe] 场上没有 VFXManager");
                return;
            }

            float groundY = CameraFitter.PilotGroundY + GroundCrackPalette.LiftY;
            // 摆在隔离带附近的空地上：卡牌是竖立 billboard，会挡住自己脚下的地面，
            // 落在卡下判断不出「有没有渲出来」（P-34 第三条）。
            float z = ClientBattle.Units.BattlefieldLayout.BeltCenterZ;
            // 左三个＝弹道三档强度，各抽不同变体遮罩以便一眼看出「不是同一张图」
            var pathKeys = GroundCrackPalette.PathVariantKeys;
            Spawn(vfx, pathKeys[0], GroundCrackPalette.Mode.Path, new Vector3(-5f, groundY, z), 35f,
                  GroundCrackPalette.Strength.Light, 1f);
            Spawn(vfx, pathKeys[1], GroundCrackPalette.Mode.Path, new Vector3(-3f, groundY, z), 35f,
                  GroundCrackPalette.Strength.Heavy, 1f);
            Spawn(vfx, pathKeys[2], GroundCrackPalette.Mode.Path, new Vector3(-1f, groundY, z), 35f,
                  GroundCrackPalette.Strength.Blaze, 1f);
            Spawn(vfx, GroundCrackPalette.ImpactMode.Key, GroundCrackPalette.Mode.Impact,
                  new Vector3(1.5f, groundY, z), null, GroundCrackPalette.Strength.Light, 1f);
            Spawn(vfx, GroundCrackPalette.ImpactMode.Key, GroundCrackPalette.Mode.Impact,
                  new Vector3(3.5f, groundY, z), null, GroundCrackPalette.Strength.Heavy, 1f);
            Spawn(vfx, GroundCrackPalette.ImpactMode.Key, GroundCrackPalette.Mode.Impact,
                  new Vector3(5.5f, groundY, z), null, GroundCrackPalette.Strength.Blaze, 1f);
            Debug.Log($"[CrackProbe] 2 模式 ×3 档已摆到 z={z:F2} 的地面，存续 6s");
        }

        static void Spawn(VFXManager vfx, string key, GroundCrackPalette.Mode modeKind,
                          Vector3 pos, float? yaw, GroundCrackPalette.Strength strength,
                          float area)
        {
            var instance = vfx.PlayAt(key, pos, 6f);
            instance.transform.rotation = yaw.HasValue
                ? Quaternion.Euler(0f, yaw.Value, 0f)
                : Quaternion.identity;
            foreach (var decal in instance.GetComponentsInChildren<GroundCrackDecal>(true))
            {
                decal.ApplyStrength(strength, modeKind);
                decal.ApplyArea(area);
                decal.Hold = 5f; // 拉长驻留，否则截图窗口只有不到一秒
            }
            var srs = instance.GetComponentsInChildren<SpriteRenderer>(true);
            Debug.Log($"[CrackProbe] {key}/{strength} @{pos} 面片={srs.Length} " +
                      (srs.Length > 0 ? $"sprite={srs[0].sprite?.name} order={srs[0].sortingOrder}" : ""));
        }
    }
}
