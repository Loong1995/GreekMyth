using System.Collections;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 神谕/被动宣告演出（client_perform §二）：
    // 「神谕类战法若有施加特效，是该战法施加完所有单位后，播放器一次给这些
    //  单位加上指定特效——播放单元仍是整个神谕战法。」
    //
    // 时间轴：施法者短前摇 → 组内全部 status_apply/attr_change 静默落账（图标、
    // 飘字）→ 同一帧给所有受影响单位挂 AuraKey 常驻光环 → 可选整盘滤镜。
    // 光环常驻（duration<=0），随整局重置一起清理。
    // Intensity 参数映射为光环透明度/滤镜浓度（后续人工在 Inspector 调）。
    // =========================================================================

    public class OracleAuraPerformance : SkillPerformance
    {
        public override IEnumerator Play(EventGroup group, PerformanceProfile profile, VFXContext ctx)
        {
            var actor = ctx.Unit(ActorOf(group));
            ctx.Sfx.Play(string.IsNullOrEmpty(profile.SfxKey) ? "sfx_oracle_default" : profile.SfxKey);
            if (actor != null)
                ctx.Vfx.PlayAt("cast_oracle", actor.transform.position, ctx.Scaled(0.4f));

            // 1. 全部副事件一次性落账（图标/飘字/属性/状态施加）。
            //    常驻光环由 UnitAuraService 按 status_id 在 status_apply 落账时统一挂上
            //    （循环粒子、随状态移除/整局重置撤下）——同帧完成，仍是"施加完所有
            //    单位后一次给这些单位加特效"的整战法播放单元。
            foreach (var ev in group.Events)
            {
                if (ev is TraitTriggerEvent) continue; // 已拆成 TraitLine 独占组
                SettleSideEvent(ev, ctx);
            }

            // 3. 整盘滤镜（海洋呼吸/血色呼吸）：程序化全屏呼吸色罩，不用粒子
            //    prefab——粒子在棋盘中心常驻会形成"固定点"且遮挡（2026-07-10 定）。
            if (!string.IsNullOrEmpty(profile.BoardFilterKey))
                BoardFilterOverlay.Attach(ctx.Board.BoardFxRoot, profile.BoardFilterKey,
                    profile.Intensity);

            // 神谕/被动宣告只触发表现，不占用主播放队列；特效自行播放。
            yield break;
        }

        static string ActorOf(EventGroup group) =>
            group.Root is SkillTriggerEvent st ? st.ActorId : null;
    }

    /// <summary>程序化整盘滤镜：全屏半透明色罩 + 正弦呼吸透明度，挂 BoardFxRoot
    /// 随整局重置清理。颜色按 key 语义取（血红/海蓝/冥紫），alpha 上限很低不遮挡。</summary>
    public class BoardFilterOverlay : MonoBehaviour
    {
        float _baseAlpha;
        SpriteRenderer _renderer;

        public static void Attach(Transform fxRoot, string key, float intensity)
        {
            // 同 key 滤镜已存在则不重复挂（同一局多次神谕宣告）
            foreach (Transform child in fxRoot)
                if (child.name == $"filter_{key}") return;

            var go = new GameObject($"filter_{key}");
            go.transform.SetParent(fxRoot, false);
            go.transform.localPosition = new Vector3(0f, 0f, 4f); // 卡牌之后、背景之前
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = FullSolidSprite(ColorOf(key));
            renderer.color = ColorOf(key);
            renderer.sortingOrder = -50;
            var overlay = go.AddComponent<BoardFilterOverlay>();
            overlay._renderer = renderer;
            // 黑底 + sRGB 编码下纯色罩感知极强（实测 alpha 0.037 已near满屏染色），
            // 基础透明度压到 0.01~0.03，只做隐约氛围
            overlay._baseAlpha = Mathf.Clamp(0.01f + intensity * 0.02f, 0.01f, 0.03f);
        }

        static Sprite _solid;

        /// <summary>无圆角整面纯色图（占位工厂的圆角方块放大后四角会漏黑）。</summary>
        static Sprite FullSolidSprite(Color color)
        {
            if (_solid == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels32(px);
                tex.Apply();
                _solid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            }
            return _solid;
        }

        static Color ColorOf(string key) =>
            key.Contains("blood") ? new Color(0.75f, 0.1f, 0.08f)
            : key.Contains("ocean") ? new Color(0.15f, 0.45f, 0.8f)
            : key.Contains("underworld") ? new Color(0.4f, 0.15f, 0.6f)
                                          : new Color(0.5f, 0.5f, 0.5f);

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic || _renderer == null) return;
            // 铺满视野（跟随 CameraFitter，机型/转屏安全）
            float h = cam.orthographicSize * 2f + 1f;
            float w = h * cam.aspect + 1f;
            var size = _renderer.sprite.bounds.size;
            transform.localScale = new Vector3(w / size.x, h / size.y, 1f);
            // 呼吸：0.6~1.0 倍基础透明度正弦摆动
            var c = _renderer.color;
            float breath = 0.8f + 0.2f * Mathf.Sin(Time.time * 1.6f);
            _renderer.color = new Color(c.r, c.g, c.b, _baseAlpha * breath);
        }
    }
}
