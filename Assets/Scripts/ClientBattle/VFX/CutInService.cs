using System.Collections;
using ClientBattle.Placeholder;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 全屏 cut-in 服务（2026-07-21）：替代旧 OnGUI 文字横幅式 cut-in。
    //
    // 1. 单人 cut-in（PlaySolo，非阻塞）：暗幕 + 阵营色斜带甩入 + 巨幅立绘
    //    反向滑入 + 大字标题，停留后整体甩出。触发源不变（满势能/高伤/追击5）。
    // 2. 决斗 cut-in（DuelClashRoutine，阻塞）：中央斜裂缝线把屏幕分成两半，
    //    两张半屏武将卡一张自上而下、一张自下而上对向滑过裂缝算一次交错；
    //    clash_cutins 次数越多（武力越接近）交错越多、一次比一次快，
    //    最后一次停在中线两侧对峙 → 裂缝闪白 →弹开。
    //
    // 渲染：世界坐标 Sprite 挂相机中心，sorting 80~90（登记于
    // docs/client/rendering_layout.md §四）；占位三级回退与全局一致。
    // =========================================================================

    public class CutInService : MonoBehaviour
    {
        public static CutInService Instance { get; private set; }

        const int OrderVeil = 80;
        const int OrderPanel = 82;
        const int OrderPortrait = 83;
        const int OrderCrack = 85;
        const int OrderFlash = 88; // 交错全屏白闪（盖住面板、低于 VS 字）
        const int OrderText = 90; // TextMesh 实际 order（NewText 内直接赋值）

        Transform _root;          // 每次演出的一次性挂点（相机中心）
        Coroutine _solo;          // 单人 cut-in 独占（新请求顶替旧的）

        public static CutInService Ensure()
        {
            if (Instance == null)
                Instance = new GameObject("CutInService").AddComponent<CutInService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>中断并清空当前 cut-in（停播/重播时由编排层调用）。</summary>
        public void CancelAll()
        {
            StopAllCoroutines();
            _solo = null;
            ClearRoot();
        }

        // ------------------------------------------------------------ 请求入口（去重）

        int _lastGroupId = -1; // 同一次结算（同组）只播 1 次

        /// <summary>重置组去重（高光回放等二次剪辑前调用，避免整场残留挡住 cut-in）。</summary>
        public void ResetDedup() => _lastGroupId = -1;

        /// <summary>cut-in 统一请求入口。heroId 非空 → 全屏单人 cut-in（暗幕+斜带+
        /// 巨幅立绘，非阻塞不占时间轴）；heroId 空（战术变更等无主体）→ BannerService
        /// 文字横幅回退 + 震屏 + BGM duck。同一播放组只播 1 次；高频满档 cut-in 为
        /// 设计意图，不做回合级限流（C10）。触发源：满档轨 momentum cut_in / 高伤 /
        /// 追伤第 5 次 / 战术变更。</summary>
        public void Request(VFXContext ctx, string heroId, string text, int groupId)
        {
            if (groupId == _lastGroupId) return;
            _lastGroupId = groupId;
            var unit = heroId != null ? ctx?.Unit(heroId) : null;
            if (unit != null)
            {
                PlaySolo(ctx, unit.Hero.TemplateId, text);
                ctx.Sfx.Play("sfx_cutin_solo");
                return;
            }
            BannerService.Ensure().ShowTextCutIn(
                text, 1.4f * Mathf.Max(0.1f, ctx?.DurationMul ?? 1f));
            CameraShaker.Shake(0.12f, 0.18f);
            Audio.BgmLayerService.Instance?.Duck(); // cut-in 全层 duck（B3）
        }

        // ------------------------------------------------------------ 单人 cut-in

        /// <summary>非阻塞单人 cut-in：斜带 + 巨幅立绘 + 标题。新请求顶替进行中的。</summary>
        public void PlaySolo(VFXContext ctx, string templateId, string title)
        {
            if (_solo != null) StopCoroutine(_solo);
            ClearRoot();
            _solo = StartCoroutine(SoloRoutine(ctx, templateId, title));
        }

        /// <summary>阻塞式满档 cut-in（2026-07-22 语义修订）：cut-in 独占时间轴，
        /// PlaybackDirector.PlayGroup 在攻击演出前 yield——切完才出手。同组去重与非阻塞路径共用。</summary>
        public IEnumerator PlaySoloBlocking(VFXContext ctx, Units.UnitView unit,
                                            string text, int groupId)
        {
            if (groupId == _lastGroupId) yield break;
            _lastGroupId = groupId;
            if (unit == null) yield break;
            if (_solo != null) { StopCoroutine(_solo); _solo = null; }
            ClearRoot();
            ctx.Sfx.Play("sfx_cutin_solo");
            Audio.BgmLayerService.Instance?.Duck();
            yield return SoloRoutine(ctx, unit.Hero.TemplateId, text);
        }

        IEnumerator SoloRoutine(VFXContext ctx, string templateId, string title)
        {
            var (halfW, halfH, center) = ScreenRect();
            _root = NewRoot(center);
            Color faction = BattleBoardView.FactionColorOf(templateId);

            var veil = NewQuad("veil", new Color(0f, 0f, 0f, 0.55f), OrderVeil,
                halfW * 2.2f, halfH * 2.2f, Vector3.zero, 0f);
            // 斜带：占屏中段约 55% 高，倾斜 12°
            var band = NewQuad("band", Fade(faction, 0.88f), OrderPanel,
                halfW * 3.2f, halfH * 1.1f, Vector3.zero, 12f);
            // 速度线示意：两条细白带（真资源可换 Resources/ClientBattle/UI/cutin_lines）
            var lineA = NewQuad("line_a", Fade(Color.white, 0.35f), OrderPortrait + 1,
                halfW * 3.2f, 0.05f, new Vector3(0f, halfH * 0.38f, 0f), 12f);
            var lineB = NewQuad("line_b", Fade(Color.white, 0.25f), OrderPortrait + 1,
                halfW * 3.2f, 0.03f, new Vector3(0f, -halfH * 0.42f, 0f), 12f);

            var portrait = NewPortrait("portrait", templateId, faction, OrderPortrait,
                halfW * 1.1f, halfH * 1.5f);
            var text = NewText("title", title, 56, Color.white);

            float dIn = ctx.Scaled(0.16f), dHold = ctx.Scaled(0.5f), dOut = ctx.Scaled(0.14f);
            Vector3 bandFrom = new(-halfW * 2.6f, -halfH * 0.3f, 0f);
            Vector3 portFrom = new(halfW * 2.2f, halfH * 0.15f, 0f);
            Vector3 portTo = new(halfW * 0.28f, 0.05f, 0f);
            Vector3 textFrom = new(-halfW * 0.4f, -halfH * 1.4f, 0f);
            Vector3 textTo = new(-halfW * 0.34f, -halfH * 0.18f, 0f);

            for (float t = 0f; t < dIn; t += Time.deltaTime)
            {
                float p = OutCubic(t / dIn);
                band.transform.localPosition = Vector3.LerpUnclamped(bandFrom, Vector3.zero, p);
                portrait.transform.localPosition = Vector3.LerpUnclamped(portFrom, portTo, p);
                text.transform.localPosition = Vector3.LerpUnclamped(textFrom, textTo, p);
                yield return null;
            }
            band.transform.localPosition = Vector3.zero;
            portrait.transform.localPosition = portTo;
            text.transform.localPosition = textTo;

            // 停留期缓慢漂移保持动势
            for (float t = 0f; t < dHold; t += Time.deltaTime)
            {
                float drift = t / dHold * halfW * 0.05f;
                portrait.transform.localPosition = portTo + new Vector3(-drift, 0f, 0f);
                band.transform.localPosition = new Vector3(drift * 0.6f, 0f, 0f);
                yield return null;
            }

            for (float t = 0f; t < dOut; t += Time.deltaTime)
            {
                float p = InCubic(t / dOut);
                Vector3 exit = new(halfW * 2.6f * p, halfH * 0.2f * p, 0f);
                band.transform.localPosition = exit;
                portrait.transform.localPosition = portTo + exit;
                text.transform.localPosition = textTo + exit;
                SetAlpha(veil, 0.55f * (1f - p));
                SetAlpha(lineA, 0.35f * (1f - p));
                SetAlpha(lineB, 0.25f * (1f - p));
                yield return null;
            }
            ClearRoot();
            _solo = null;
        }

        // ------------------------------------------------------------ 决斗 cut-in

        /// <summary>阻塞式决斗交错 cut-in：passes 次对向滑过中央裂缝，
        /// 一次比一次快；末次两卡停在裂缝两侧对峙后弹开。onClash 每次交错回调
        /// （编排层放音效/震屏）。</summary>
        public IEnumerator DuelClashRoutine(
            VFXContext ctx, UnitView left, UnitView right, int passes,
            System.Action onClash)
        {
            if (_solo != null) { StopCoroutine(_solo); _solo = null; }
            ClearRoot();
            var (halfW, halfH, center) = ScreenRect();
            _root = NewRoot(center);
            passes = Mathf.Clamp(passes, 1, 3);

            Color colorL = BattleBoardView.FactionColorOf(left.Hero.TemplateId);
            Color colorR = BattleBoardView.FactionColorOf(right.Hero.TemplateId);

            NewQuad("veil", new Color(0f, 0f, 0f, 0.7f), OrderVeil,
                halfW * 2.2f, halfH * 2.2f, Vector3.zero, 0f);

            // 中央裂缝线：偏竖直 8°，贯穿全屏
            var crack = NewQuad("crack", Fade(Color.white, 0.9f), OrderCrack,
                0.07f, halfH * 2.4f, Vector3.zero, 8f);
            var crackGlow = NewQuad("crack_glow", Fade(Color.white, 0.25f), OrderCrack - 1,
                0.3f, halfH * 2.4f, Vector3.zero, 8f);
            // 交错白闪（与 cut-in 同层几何，不用 RFX 粒子）
            var flash = NewQuad("clash_flash", Fade(Color.white, 0f), OrderFlash,
                halfW * 2.2f, halfH * 2.2f, Vector3.zero, 0f);

            // 两张半屏卡：阵营色底板 + 巨幅立绘 + 名字
            var panelL = BuildDuelPanel("panel_L", left, colorL, halfW, halfH, -1);
            var panelR = BuildDuelPanel("panel_R", right, colorR, halfW, halfH, +1);

            float travel = halfH * 2.6f;                  // 越屏行程
            float duration = ctx.Scaled(0.34f);           // 首次交错时长
            for (int i = 0; i < passes; i++)
            {
                bool last = i == passes - 1;
                // 交替方向：偶数次左卡从上往下，奇数次反向（来回交错感）
                float dir = i % 2 == 0 ? 1f : -1f;
                bool clashed = false;
                for (float t = 0f; t < duration; t += Time.deltaTime)
                {
                    float p = t / duration; // 线性滑过（高速掠过感）
                    float yL = Mathf.Lerp(travel * dir, -travel * dir, p);
                    panelL.localPosition = new Vector3(-halfW * 0.5f, yL, 0f);
                    panelR.localPosition = new Vector3(halfW * 0.5f, -yL, 0f);
                    if (!clashed && p >= 0.5f)
                    {
                        clashed = true;
                        onClash?.Invoke();
                        StartCoroutine(FlashClash(crack, crackGlow, flash));
                    }
                    yield return null;
                }
                duration *= 0.72f; // 一次比一次快（武力接近 → 多次高速交错）
                if (last)
                {
                    // 末次：拉回中线两侧对峙
                    float dHold = ctx.Scaled(0.12f);
                    Vector3 fromL = panelL.localPosition, fromR = panelR.localPosition;
                    Vector3 toL = new(-halfW * 0.5f, halfH * 0.06f, 0f);
                    Vector3 toR = new(halfW * 0.5f, -halfH * 0.06f, 0f);
                    for (float t = 0f; t < dHold; t += Time.deltaTime)
                    {
                        float p = OutCubic(t / dHold);
                        panelL.localPosition = Vector3.LerpUnclamped(fromL, toL, p);
                        panelR.localPosition = Vector3.LerpUnclamped(fromR, toR, p);
                        yield return null;
                    }
                    var vs = NewText("vs", "VS", 88, Color.white);
                    vs.transform.localPosition = Vector3.zero;
                    onClash?.Invoke();
                    yield return FlashClash(crack, crackGlow, flash);
                    yield return new WaitForSeconds(ctx.Scaled(0.45f));
                }
            }

            // 弹开退场
            float dOut = ctx.Scaled(0.16f);
            Vector3 outL0 = panelL.localPosition, outR0 = panelR.localPosition;
            for (float t = 0f; t < dOut; t += Time.deltaTime)
            {
                float p = InCubic(t / dOut);
                panelL.localPosition = outL0 + new Vector3(-halfW * 1.6f * p, 0f, 0f);
                panelR.localPosition = outR0 + new Vector3(halfW * 1.6f * p, 0f, 0f);
                yield return null;
            }
            ClearRoot();
        }

        Transform BuildDuelPanel(
            string name, UnitView unit, Color faction, float halfW, float halfH, int side)
        {
            var panel = new GameObject(name).transform;
            panel.SetParent(_root, false);
            var back = NewQuad($"{name}_back", Fade(Color.Lerp(faction, Color.black, 0.25f), 0.92f),
                OrderPanel, halfW * 1.04f, halfH * 2.4f, Vector3.zero, 0f);
            back.transform.SetParent(panel, false);
            var portrait = NewPortrait($"{name}_portrait", unit.Hero.TemplateId, faction,
                OrderPortrait, halfW * 0.9f, halfH * 1.6f);
            portrait.transform.SetParent(panel, false);
            portrait.transform.localPosition = new Vector3(0f, halfH * 0.08f, 0f);
            var label = NewText($"{name}_name", unit.Hero.HeroId, 44, Color.white);
            label.transform.SetParent(panel, false);
            label.transform.localPosition = new Vector3(0f, -halfH * 0.62f, 0f);
            panel.localPosition = new Vector3(halfW * 0.5f * side, 0f, 0f);
            return panel;
        }

        IEnumerator FlashClash(SpriteRenderer crack, SpriteRenderer glow, SpriteRenderer flash)
        {
            for (float t = 0f; t < 0.22f; t += Time.deltaTime)
            {
                float u = t / 0.22f;
                float a = 1f - u;
                if (crack != null) SetAlpha(crack, 0.9f + a * 0.1f);
                if (glow != null)
                {
                    SetAlpha(glow, 0.25f + a * 0.6f);
                    glow.transform.localScale = new Vector3(
                        0.3f * (1f + a * 2.2f), glow.transform.localScale.y, 1f);
                }
                // 前半段冲到 0.55 白，后半段淡出——干净的撞击感，无粒子
                if (flash != null)
                    SetAlpha(flash, u < 0.35f ? Mathf.Lerp(0f, 0.55f, u / 0.35f)
                                             : Mathf.Lerp(0.55f, 0f, (u - 0.35f) / 0.65f));
                yield return null;
            }
            if (flash != null) SetAlpha(flash, 0f);
        }

        IEnumerator FlashCrack(SpriteRenderer crack, SpriteRenderer glow)
        {
            yield return FlashClash(crack, glow, null);
        }

        // ------------------------------------------------------------ 构件

        (float halfW, float halfH, Vector3 center) ScreenRect()
        {
            var cam = Camera.main;
            float halfH = CameraFitter.VisibleHalfHeightAt(cam, 0f);
            float halfW = halfH * (cam != null ? cam.aspect : 1.78f);
            Vector3 center = cam != null
                ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f)
                : Vector3.zero;
            return (halfW, halfH, center);
        }

        Transform NewRoot(Vector3 center)
        {
            var root = new GameObject("cutin_root").transform;
            root.SetParent(transform, false);
            root.position = center;
            return root;
        }

        void ClearRoot()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
        }

        SpriteRenderer NewQuad(string name, Color color, int order,
                               float width, float height, Vector3 pos, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderFactory.MakeSolidSprite(Color.white, 8);
            sr.color = color;
            sr.sortingOrder = order;
            var size = sr.sprite.bounds.size;
            go.transform.localScale = new Vector3(width / size.x, height / size.y, 1f);
            return sr;
        }

        SpriteRenderer NewPortrait(string name, string templateId, Color faction,
                                   int order, float slotW, float slotH)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PlaceholderFactory.GetSprite(
                "Portraits", templateId, Color.Lerp(faction, Color.black, 0.35f), 96);
            sr.sortingOrder = order;
            var size = sr.sprite.bounds.size; // contain 等比放进槽位（与卡面立绘同规则）
            float scale = Mathf.Min(slotW / size.x, slotH / size.y);
            go.transform.localScale = Vector3.one * scale;
            return sr;
        }

        TextMesh NewText(string name, string content, int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var mesh = go.AddComponent<TextMesh>();
            var font = FloatingTextTuning.LoadOrDefault().ResolveFont();
            if (font != null)
            {
                mesh.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }
            mesh.text = content;
            mesh.fontSize = fontSize;
            mesh.color = color;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.06f;
            go.GetComponent<MeshRenderer>().sortingOrder = OrderText;
            return mesh;
        }

        static Color Fade(Color c, float a) => new(c.r, c.g, c.b, a);
        static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr != null) sr.color = Fade(sr.color, a);
        }

        static float OutCubic(float p) => 1f - Mathf.Pow(1f - Mathf.Clamp01(p), 3f);
        static float InCubic(float p) => Mathf.Pow(Mathf.Clamp01(p), 3f);
    }
}
