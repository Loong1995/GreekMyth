using ClientBattle.Placeholder;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第5层 基础设施】卡牌接地阴影（近 3D 舞台专用）。
    //
    // 为什么必须有：本项目是 45° 后倾的近 3D 舞台、有真实地面，但卡牌此前
    // 没有落地阴影。**没有接触阴影的物体，人眼一律判定为"浮在空中的贴纸"** ——
    // 这是"卡牌呆板"最物理层的一个来源，且与立绘品质无关。
    //
    // 形状：椭圆软影，长宽取自 ArenaSlotLayout 的卡牌地面足迹
    // （宽＝CardWidth，纵深＝CardShadowDepth＝卡高在地面的投影），
    // 所以卡尺一改阴影自动跟随，不写死世界单位。
    //
    // 抬升响应：卡牌离地越高，影子越小越淡（接触感的唯一来源）。
    // sorting −3：地面背景（−100）之上、残影（−2）与卡牌（0/1）之下。
    //
    // 正交模式（无近 3D 地面）不创建，整套零开销。
    // 文档：docs/client/rendering_layout.md §四
    // =========================================================================

    public sealed class CardGroundShadow : MonoBehaviour
    {
        const int ShadowOrder = -3;

        static Sprite _sharedSprite;

        UnitView _unit;
        SpriteRenderer _renderer;
        Transform _shadow;
        float _defeatFade = 1f;

        /// <summary>建卡时调用。非近 3D 舞台直接返回 null（不建对象、不进 LateUpdate）。</summary>
        public static CardGroundShadow AttachTo(UnitView unit)
        {
            if (unit == null || !ArenaSlotLayout.GroundActive) return null;
            var comp = unit.gameObject.AddComponent<CardGroundShadow>();
            comp.Build(unit);
            return comp;
        }

        void Build(UnitView unit)
        {
            _unit = unit;
            var go = new GameObject($"{unit.name}_shadow");
            // 挂到卡牌的父级而非卡牌自身：卡牌后倾 45° 且会被 DOPunchScale 缩放，
            // 做子物体会把倾角与缩放继承下来，影子就不再平躺在地上了
            go.transform.SetParent(unit.transform.parent, false);
            _shadow = go.transform;
            _renderer = go.AddComponent<SpriteRenderer>();
            // 上传同名图即替换（占位三级回退惯例）
            _renderer.sprite = PlaceholderFactory.TryLoadSprite("CardFrames", "card_shadow")
                               ?? SharedSprite();
            _renderer.sortingOrder = ShadowOrder;
            Sync();
        }

        void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }

        void LateUpdate()
        {
            // LateUpdate：必须在 UnitView.Update（呼吸）与 DOTween 位移都写完之后取位置，
            // 否则影子永远慢卡牌一帧，快速突进时会明显脱节
            if (_unit == null || _shadow == null) return;
            if (_unit.Defeated && _defeatFade > 0f)
                _defeatFade = Mathf.Max(0f, _defeatFade - Time.deltaTime
                    / Mathf.Max(0.01f, StagePerformanceConfig.ShadowDefeatFadeSeconds));
            Sync();
        }

        void Sync()
        {
            Vector3 card = _unit.transform.position;
            Vector3 foot = ArenaSlotLayout.GroundFoot(card);
            _shadow.SetPositionAndRotation(foot, Quaternion.Euler(90f, 0f, 0f));

            // 抬升量 = 当前卡心高度 − 该接地点上「贴地站好」时的卡心高度
            float restY = ArenaSlotLayout.GroundPoint(foot.x, foot.z).y;
            float halfCard = Mathf.Max(0.01f,
                StanceLayout.CardHeight * StanceLayout.ChromeFactor * 0.5f);
            float lift = Mathf.Clamp01(Mathf.Max(0f, card.y - restY) / halfCard);

            float w = StanceLayout.CardWidth * StagePerformanceConfig.ShadowWidthRatio;
            float d = ArenaSlotLayout.CardShadowDepth * StagePerformanceConfig.ShadowDepthRatio;
            float k = Mathf.Lerp(1f, StagePerformanceConfig.ShadowLiftMinScale, lift);
            _shadow.localScale = new Vector3(w * k, d * k, 1f);

            float a = StagePerformanceConfig.ShadowAlpha
                      * Mathf.Lerp(1f, StagePerformanceConfig.ShadowLiftMinAlpha, lift)
                      * _defeatFade;
            _renderer.color = new Color(0f, 0f, 0f, a);
        }

        /// <summary>程序化软椭圆：中心实、边缘平方衰减（接触点最黑，符合接触阴影）。
        /// pixelsPerUnit＝size 使 sprite 边界恰为 1×1 世界单位，缩放即可直接写宽/深。</summary>
        static Sprite SharedSprite()
        {
            if (_sharedSprite != null) return _sharedSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                name = "card_shadow_procedural",
            };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - r) / r;
                    float dy = (y + 0.5f - r) / r;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte a = (byte)(falloff * falloff * 255f);
                    px[y * size + x] = new Color32(0, 0, 0, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true); // 上传后弃读，省一份内存
            _sharedSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size),
                                          new Vector2(0.5f, 0.5f), size);
            return _sharedSprite;
        }
    }
}
