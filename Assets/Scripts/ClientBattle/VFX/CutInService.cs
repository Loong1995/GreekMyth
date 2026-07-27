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
    // 2. 单挑 cut-in（DuelClashRoutine，阻塞）：立绘出框 → 虚空展示屏 → 交错与
    //    动作 ×clash_cutins → 定胜负 → 飞回卡框。实现在 DuelStage.cs，本类只
    //    负责独占仲裁（顶掉进行中的 solo）、建/毁挂点、以及中断时的还原。
    //
    // 渲染：世界坐标 Sprite 挂在**相机正前方**一块随相机旋转的平面上
    // （见 ScreenRect），sorting 80~93（登记于 docs/client/rendering_layout.md §四）；
    // 占位三级回退与全局一致。
    // =========================================================================

    public class CutInService : MonoBehaviour
    {
        public static CutInService Instance { get; private set; }

        const int OrderVeil = 80;
        const int OrderPanel = 82;
        const int OrderPortrait = 83;
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
            // 单挑期间卡面立绘被藏起、由飞行替身代演；中断路径必须还原，
            // 否则停播/重播后战场上会留下两张没有立绘的空卡框。
            _duel?.Restore();
            _duel = null;
            // 同理：推镜也必须还，否则战斗剩余部分会一直卡在推近的机位上。
            StageCameraRig.ReleaseAll();
            ClearRoot();
        }

        // ------------------------------------------------------------ 请求入口（去重）

        int _lastGroupId = -1; // 同一次结算（同组）只播 1 次

        /// <summary>重置组去重（高光回放等二次剪辑前调用，避免整场残留挡住 cut-in）。</summary>
        public void ResetDedup() => _lastGroupId = -1;

        /// <summary>该组是否已切过 cut-in。<see cref="CutInStage"/> 用它避免
        /// 「已去重却仍推了一次镜头」的空运镜。</summary>
        public bool AlreadyPlayed(int groupId) => groupId == _lastGroupId;

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
            var (halfW, halfH) = ScreenRect();
            _root = NewRoot();
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

        DuelStage _duel;

        /// <summary>阻塞式单挑舞台 cut-in（2026-07-27 重做）：两名参战武将的立绘
        /// **从各自卡框飞出**，落进中央虚空展示屏，交错 passes 轮、每轮打一段动作
        /// （flipbook；缺帧则静态立绘占满该段），分出胜负后飞回卡框。
        ///
        /// 分幕与素材约定见 <see cref="DuelStage"/> 类头；数值在 StagePerformanceConfig。
        /// onClash 每次交错回调一次（编排层放音效/震屏）。</summary>
        public IEnumerator DuelClashRoutine(
            VFXContext ctx, UnitView left, UnitView right, int passes,
            string winnerId, System.Action onClash)
        {
            if (_solo != null) { StopCoroutine(_solo); _solo = null; }
            ClearRoot();
            _root = NewRoot();
            _duel = new DuelStage();
            yield return _duel.Run(ctx, _root, left, right, passes, winnerId, onClash);
            _duel = null;
            ClearRoot();
        }

        // ------------------------------------------------------------ 构件

        /// <summary>全屏 cut-in 的取景基准：**相机正前方固定距离**那块平面的半宽半高。
        /// 所有 cut-in 构件都挂在这块平面的局部坐标里，于是「屏幕左/右/上/下」
        /// 在任何相机俯角下都成立。
        ///
        /// 【勿退回旧写法】旧实现取 `(cam.x, cam.y, 0)` 且不带旋转，隐含假设
        /// 「相机平视、看向 −Z」。相机俯角一改（现 35°，位置约 (0,31.5,−45)、
        /// FOV≈12°），该点离光轴 35° → 整个 cut-in 飞出视锥不可见（P-64）。</summary>
        internal static (float halfW, float halfH) ScreenRect()
        {
            var cam = Camera.main;
            if (cam == null) return (9.2f, 5.2f);
            float halfH = cam.orthographic
                ? cam.orthographicSize
                : CutInDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return (halfH * cam.aspect, halfH);
        }

        /// <summary>cut-in 平面到相机的距离。取值无观感影响（半高按同一距离反算，
        /// 且 sorting 80~93 保证盖住一切），只需落在 near/far 之间。</summary>
        internal const float CutInDistance = 12f;

        /// <summary>建挂点：**挂到相机身上**，而不是摆一个世界坐标。
        ///
        /// 为什么必须是父子关系而不是"算一次位置"：单挑期间 <see cref="StageCameraRig"/>
        /// 会把相机推近、抬俯角。挂点若是世界坐标，相机一动整块屏就滑出视野；
        /// 作为相机子物体则天然跟随，运镜与 cut-in 彻底解耦。
        /// 顺带：相机抖动（Shake 动的是相机 localPosition）不会传到屏上——
        /// 全屏构件本来就该稳在屏幕上、由世界去抖。</summary>
        Transform NewRoot()
        {
            var cam = Camera.main;
            var root = new GameObject("cutin_root").transform;
            root.SetParent(cam != null ? cam.transform : transform, false);
            root.localPosition = new Vector3(0f, 0f, CutInDistance);
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
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
