using ClientBattle.Placeholder;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】单挑展示屏的**华饰与氛围层**：暗幕、影院黑边、屏体、
    // 双色阵营辉光、放射光芒、浮尘余烬、四角纹饰、中央图标、冲击环与白闪。
    //
    // 为什么单独一个类、而且是 MonoBehaviour：
    //   · 职责分离——DuelStage 只管**编排**（谁什么时候飞到哪、打哪一拍），
    //     本类只管**屏怎么好看**。两者混在一起，加一件装饰就要动编排代码。
    //   · **自走时钟**——挂 MonoBehaviour 靠 Update 自转，于是无论编排层此刻在
    //     插值、在等 WaitForSeconds、还是在放 flipbook，屏上永远有东西在动。
    //     这是零死帧（R-4.1）在这块屏上的兑现方式：**不依赖编排层记得每帧调它**。
    //
    // 「呆板」的病根与药方（这一层解决的就是这个）：
    //   静止的纯色底 + 静止的立绘 = 一张贴纸。人眼判定「活」靠的是**多个速率不同
    //   的运动叠在一起**。所以这里刻意铺了四种周期：放射光芒的**慢转**、
    //   浮尘的**匀速上升 + 横向摆动**、边框的**呼吸**、整屏的**极缓推进**。
    //   四者周期互质，任何两帧都不重样，画面就"呼吸"起来了。
    //
    // 全程零预制资源：所有贴图程序化生成（纯色/渐变/环/软点），同名真图存在则
    // 自动顶替（占位三级回退）。可换真图的 key 见各 Build* 方法。
    // 参数一律在 StagePerformanceConfig（Duel* 段）。
    // 文档：docs/mechanics/duel.md §5b、docs/client/rendering_layout.md §四
    // =========================================================================

    public sealed class DuelStageChrome : MonoBehaviour
    {
        // 层号总表（与 DuelStage.OrderPortrait=88 / 背光 87 连成一摞，
        // 登记于 docs/client/rendering_layout.md §四）。**加装饰先来这里占号**，
        // 撞号在同层内的先后由距离/建序决定，会随机闪。
        const int OrderVeil = 80;
        const int OrderScreenRim = 81;   // 边框（整块，被 inner 盖住只露一圈边）
        const int OrderScreenInner = 82; // 屏底（略小于边框）
        const int OrderFactionGlow = 83; // 左右阵营辉光
        const int OrderRays = 84;        // 放射光芒
        const int OrderCorner = 85;      // 四角纹饰
        const int OrderEmberBack = 86;   // 立绘之后的浮尘
        const int OrderEmberFront = 89;  // 立绘之前的浮尘（前后都有才有纵深）
        const int OrderIcon = 90;
        const int OrderRing = 91;
        const int OrderFlash = 92;
        const int OrderLetterbox = 93;   // 黑边压在一切之上（它是"画框"）

        /// <summary>每帧附加回调，`dt` 为本帧时长。DuelStage 把两名飞行体的
        /// 待机呼吸挂在这里——让呼吸和屏体氛围共用同一条自走时钟，
        /// 编排层就不必在每个插值循环里手动 Tick。</summary>
        public System.Action<float> OnTick;

        Transform _screen;               // 屏体（开合 + 推进都作用在它身上）
        SpriteRenderer _veil, _rim, _inner, _icon, _ring, _flash;
        SpriteRenderer _letterTop, _letterBot;
        SpriteRenderer[] _corners;
        Transform _rayL, _rayR;
        SpriteRenderer[] _rayQuads;
        Ember[] _embers;

        float _halfW, _halfH;
        float _open, _iconOpen;
        float _time;
        float _pulse;                    // 交错脉冲剩余（驱动白闪/冲击环/边框冲高）
        float _ringLife;
        Vector3 _iconBaseScale;
        float _emberTop, _emberBottom;

        sealed class Ember
        {
            public Transform Tr;
            public SpriteRenderer Sr;
            public float BaseX, Y, Speed, SwayAmp, SwayHz, Phase, Alpha, Size;
        }

        // ------------------------------------------------------------ 构建

        public static DuelStageChrome Build(Transform root, float halfW, float halfH,
                                            Color colorL, Color colorR)
        {
            var go = new GameObject("duel_chrome");
            go.transform.SetParent(root, false);
            var chrome = go.AddComponent<DuelStageChrome>();
            chrome.Construct(root, halfW, halfH, colorL, colorR);
            return chrome;
        }

        void Construct(Transform root, float halfW, float halfH, Color colorL, Color colorR)
        {
            _halfW = halfW;
            _halfH = halfH;

            _veil = Quad(root, "veil", Solid(), Fade(Color.black, 0f), OrderVeil,
                halfW * 2.4f, halfH * 2.4f);

            _screen = new GameObject("screen").transform;
            _screen.SetParent(root, false);
            float scrW = halfW * StagePerformanceConfig.DuelScreenWidth;
            float scrH = halfH * StagePerformanceConfig.DuelScreenHeight;

            _rim = Quad(_screen, "rim", Solid(), Fade(RimColor, 0f), OrderScreenRim, scrW, scrH);
            _inner = Quad(_screen, "inner",
                PlaceholderFactory.TryLoadSprite("UI", "duel_screen_bg") ?? Solid(),
                Fade(InnerColor, 0f), OrderScreenInner,
                scrW - halfW * 0.02f, scrH - halfH * 0.03f);

            // 双色阵营辉光：左右两侧各一条横向渐变，向屏心衰减。
            // 一眼就能读出"这半边是谁"，且比纯黑底有层次得多。
            float glowW = scrW * 0.5f, glowH = scrH * 0.94f;
            var glowL = Quad(_screen, "glow_L", Ramp(), Fade(colorL, 0f), OrderFactionGlow,
                glowW, glowH);
            glowL.transform.localPosition = new Vector3(-scrW * 0.25f, 0f, 0f);
            var glowR = Quad(_screen, "glow_R", Ramp(), Fade(colorR, 0f), OrderFactionGlow,
                glowW, glowH);
            glowR.transform.localPosition = new Vector3(scrW * 0.25f, 0f, 0f);
            glowR.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // 渐变朝向屏心
            _glows = new[] { glowL, glowR };

            _rayL = BuildRays("rays_L", -1, colorL);
            _rayR = BuildRays("rays_R", +1, colorR);

            BuildCorners(scrW, scrH);
            BuildEmbers(scrW, scrH);

            float iconSize = halfH * StagePerformanceConfig.DuelIconSize;
            _icon = Quad(root, "icon",
                PlaceholderFactory.GetSprite("UI", "duel_icon", IconColor, 96),
                Fade(Color.white, 0f), OrderIcon, 0f, 0f);
            Contain(_icon, iconSize, iconSize);
            _iconBaseScale = _icon.transform.localScale;

            _ring = Quad(root, "impact_ring", Ring(), Fade(Color.white, 0f), OrderRing,
                iconSize, iconSize);
            _flash = Quad(root, "flash", Solid(), Fade(Color.white, 0f), OrderFlash,
                halfW * 2.4f, halfH * 2.4f);

            // 影院黑边：进场压下、退场收起。这是最省事也最有效的"过场"信号——
            // 观众不需要被告知"现在是重头戏"，画幅变了自己就知道。
            float barH = halfH * StagePerformanceConfig.DuelLetterboxHeight;
            _letterTop = Quad(root, "letterbox_top", Solid(), Color.black, OrderLetterbox,
                halfW * 2.4f, barH);
            _letterBot = Quad(root, "letterbox_bottom", Solid(), Color.black, OrderLetterbox,
                halfW * 2.4f, barH);

            SetOpen(0f);
            SetIcon(0f);
        }

        SpriteRenderer[] _glows;

        /// <summary>放射光芒：N 条细长条各转 180/N 度拼成 2N 道光。
        /// 半径按半高算，保证整个光盘落在屏内（无遮罩，出屏会糊到暗幕上）。
        /// 真图 `UI/duel_rays` 存在则用单张顶替，省 N 个 SpriteRenderer。</summary>
        Transform BuildRays(string name, int side, Color faction)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(_screen, false);
            pivot.localPosition = new Vector3(
                side * _halfW * StagePerformanceConfig.DuelSlotX, 0f, 0f);

            float radius = _halfH * StagePerformanceConfig.DuelRayRadius;
            var real = PlaceholderFactory.TryLoadSprite("UI", "duel_rays");
            if (real != null)
            {
                var one = Quad(pivot, "rays", real, Fade(faction, 0f), OrderRays,
                    radius * 2f, radius * 2f);
                _rayQuads = Append(_rayQuads, one);
                return pivot;
            }

            int n = Mathf.Max(3, StagePerformanceConfig.DuelRayCount);
            for (int i = 0; i < n; i++)
            {
                var ray = Quad(pivot, $"ray_{i}", Solid(), Fade(faction, 0f),
                    OrderRays, _halfH * 0.022f, radius * 2f);
                ray.transform.localRotation = Quaternion.Euler(0f, 0f, i * 180f / n);
                _rayQuads = Append(_rayQuads, ray);
            }
            return pivot;
        }

        /// <summary>四角纹饰。真图 `UI/duel_corner` 存在则用（画左上角朝向即可，
        /// 其余三角由代码旋转复用）；否则退化为小色块——聊胜于无，但不假。</summary>
        void BuildCorners(float scrW, float scrH)
        {
            var sprite = PlaceholderFactory.TryLoadSprite("UI", "duel_corner");
            float size = _halfH * StagePerformanceConfig.DuelCornerSize;
            _corners = new SpriteRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                int sx = i % 2 == 0 ? -1 : 1;
                int sy = i < 2 ? 1 : -1;
                var sr = Quad(_screen, $"corner_{i}", sprite ?? Solid(),
                    Fade(RimColor, 0f), OrderCorner, size, size);
                sr.transform.localPosition = new Vector3(
                    sx * (scrW * 0.5f - size * 0.5f), sy * (scrH * 0.5f - size * 0.5f), 0f);
                sr.transform.localScale = new Vector3(
                    sr.transform.localScale.x * sx, sr.transform.localScale.y * sy, 1f);
                _corners[i] = sr;
            }
        }

        /// <summary>浮尘余烬：屏内匀速上升 + 横向正弦摆动，出顶回底循环。
        /// 前后各一半（分处立绘之前/之后的 sorting），才有纵深而不是一层贴纸。</summary>
        void BuildEmbers(float scrW, float scrH)
        {
            int n = Mathf.Max(0, StagePerformanceConfig.DuelEmberCount);
            _emberBottom = -scrH * 0.5f;
            _emberTop = scrH * 0.5f;
            _embers = new Ember[n];
            for (int i = 0; i < n; i++)
            {
                float size = _halfH * Random.Range(0.008f, 0.022f);
                var sr = Quad(_screen, $"ember_{i}", Dot(), Fade(EmberColor, 0f),
                    i % 2 == 0 ? OrderEmberBack : OrderEmberFront, size, size);
                var e = new Ember
                {
                    Tr = sr.transform,
                    Sr = sr,
                    BaseX = Random.Range(-scrW * 0.46f, scrW * 0.46f),
                    Y = Random.Range(_emberBottom, _emberTop),
                    Speed = _halfH * StagePerformanceConfig.DuelEmberRiseSpeed
                            * Random.Range(0.6f, 1.5f),
                    SwayAmp = _halfH * Random.Range(0.01f, 0.05f),
                    SwayHz = Random.Range(0.15f, 0.5f),
                    Phase = Random.Range(0f, 10f),
                    Alpha = Random.Range(0.35f, 1f),
                    Size = size,
                };
                _embers[i] = e;
                e.Tr.localPosition = new Vector3(e.BaseX, e.Y, 0f);
            }
        }

        // ------------------------------------------------------------ 编排接口

        /// <summary>开合进度 0~1：暗幕、屏体纵向展开、边框/辉光/光芒/纹饰淡入、
        /// 影院黑边推入。出场与退场共用（退场传 1→0），所以一定对称。</summary>
        public void SetOpen(float p)
        {
            _open = Mathf.Clamp01(p);

            // 暗幕**滞后**于屏体开合（DuelVeilDelay）。出框时观众要先在真战场上
            // 看见两名武将脚下的出阵特效，世界才暗下去；回程时暗幕先散，
            // 胜负特效才落在看得见的战场上。这两件都在 sorting 80 的暗幕之下，
            // 不留这段窗口就等于白播。
            float delay = Mathf.Clamp(StagePerformanceConfig.DuelVeilDelay, 0f, 0.9f);
            float veil = Mathf.Clamp01((_open - delay) / (1f - delay));
            SetAlpha(_veil, StagePerformanceConfig.DuelVeilAlpha * veil);
            SetAlpha(_rim, 0.85f * _open);
            SetAlpha(_inner, 0.94f * _open);
            foreach (var glow in _glows)
                SetAlpha(glow, StagePerformanceConfig.DuelFactionGlowAlpha * _open);
            foreach (var ray in _rayQuads)
                SetAlpha(ray, StagePerformanceConfig.DuelRayAlpha * _open);
            foreach (var corner in _corners)
                SetAlpha(corner, 0.9f * _open);
            foreach (var e in _embers)
                SetAlpha(e.Sr, StagePerformanceConfig.DuelEmberAlpha * e.Alpha * _open);

            float barH = _halfH * StagePerformanceConfig.DuelLetterboxHeight;
            float edge = _halfH + barH * 0.5f;
            _letterTop.transform.localPosition = new Vector3(
                0f, Mathf.LerpUnclamped(edge, _halfH - barH * 0.5f, _open), 0f);
            _letterBot.transform.localPosition = new Vector3(
                0f, Mathf.LerpUnclamped(-edge, -_halfH + barH * 0.5f, _open), 0f);
        }

        /// <summary>中央单挑图标的浮现进度 0~1（缩放从 1.8 倍砸到 1 倍）。</summary>
        public void SetIcon(float p)
        {
            _iconOpen = Mathf.Clamp01(p);
            SetAlpha(_icon, _iconOpen);
            _icon.transform.localScale =
                _iconBaseScale * Mathf.LerpUnclamped(1.8f, 1f, _iconOpen);
        }

        /// <summary>交错命中/定胜负：白闪 + 中央冲击环 + 边框能量冲高。
        /// 只置计时器，具体衰减在 Update 里跑——不起协程，硬停止时随对象一起没。</summary>
        public void Pulse()
        {
            _pulse = 1f;
            _ringLife = 1f;
        }

        // ------------------------------------------------------------ 自走时钟

        void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;
            OnTick?.Invoke(dt);

            // 屏体：纵向开合 × 极缓推进。推进量很小（默认 1.045），
            // 单看每一帧察觉不到，连起来就是"镜头在压过来"。
            float push = Mathf.Lerp(1f, StagePerformanceConfig.DuelPushInScale,
                Mathf.Clamp01(_time / Mathf.Max(0.1f, StagePerformanceConfig.DuelPushInSeconds)));
            _screen.localScale = new Vector3(
                push, Mathf.LerpUnclamped(0.03f, 1f, _open) * push, 1f);

            // 放射光芒慢转，左右反向——同向转会读成整体在旋，反向才有对抗感
            float spin = StagePerformanceConfig.DuelRaySpinDegPerSec * dt;
            _rayL.localRotation *= Quaternion.Euler(0f, 0f, spin);
            _rayR.localRotation *= Quaternion.Euler(0f, 0f, -spin);

            TickEmbers(dt);

            // 边框呼吸 + 脉冲冲高
            if (_pulse > 0f) _pulse = Mathf.Max(0f, _pulse - dt / PulseSeconds);
            float breath = 0.5f + 0.5f * Mathf.Sin(
                _time * StagePerformanceConfig.DuelRimBreathHz * Mathf.PI * 2f);
            SetAlpha(_rim, _open * (0.72f + 0.16f * breath + 0.5f * _pulse));

            // 白闪：前 35% 冲起、后 65% 淡出（干净的撞击感，不用粒子）
            float u = 1f - _pulse;
            SetAlpha(_flash, _pulse <= 0f ? 0f
                : StagePerformanceConfig.DuelClashFlashAlpha
                  * (u < 0.35f ? u / 0.35f : 1f - (u - 0.35f) / 0.65f));

            TickRing(dt);
        }

        void TickEmbers(float dt)
        {
            float span = _emberTop - _emberBottom;
            foreach (var e in _embers)
            {
                e.Y += e.Speed * dt;
                if (e.Y > _emberTop) e.Y -= span;
                e.Tr.localPosition = new Vector3(
                    e.BaseX + Mathf.Sin(_time * e.SwayHz * Mathf.PI * 2f + e.Phase) * e.SwayAmp,
                    e.Y, 0f);
            }
        }

        void TickRing(float dt)
        {
            if (_ringLife <= 0f) return;
            _ringLife = Mathf.Max(0f,
                _ringLife - dt / Mathf.Max(0.05f, StagePerformanceConfig.DuelImpactRingSeconds));
            float k = 1f - _ringLife; // 0→1 扩张
            float size = _halfH * StagePerformanceConfig.DuelIconSize
                         * Mathf.LerpUnclamped(0.6f, StagePerformanceConfig.DuelImpactRingScale, k);
            Contain(_ring, size, size);
            SetAlpha(_ring, _ringLife * 0.8f);
        }

        const float PulseSeconds = 0.28f;

        // ------------------------------------------------------------ 构件与贴图

        static readonly Color RimColor = new(0.58f, 0.76f, 1f);
        static readonly Color InnerColor = new(0.03f, 0.04f, 0.07f);
        static readonly Color IconColor = new(0.96f, 0.86f, 0.52f);
        static readonly Color EmberColor = new(1f, 0.86f, 0.62f);

        // 程序化贴图一次生成、全局复用。Unity 判空：退出 Play 会销毁运行期资源，
        // 跨会话静态缓存必须重建（与 PlaceholderFactory 同一约定）。
        static Sprite _solidSprite, _rampSprite, _ringSprite, _dotSprite;

        static Sprite Solid()
        {
            if (_solidSprite == null) _solidSprite = PlaceholderFactory.MakeSolidSprite(Color.white, 8);
            return _solidSprite;
        }

        /// <summary>横向 alpha 渐变（左实右虚），用于阵营辉光。</summary>
        static Sprite Ramp()
        {
            if (_rampSprite != null) return _rampSprite;
            const int w = 64;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            {
                float a = 1f - (float)x / (w - 1);
                tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a * a)); // 平方衰减更柔
            }
            tex.Apply();
            _rampSprite = Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f), w);
            return _rampSprite;
        }

        /// <summary>细环（冲击波）。</summary>
        static Sprite Ring()
        {
            if (_ringSprite != null) return _ringSprite;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            float c = (s - 1) * 0.5f, outer = c, inner = c * 0.82f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = d > outer || d < inner
                        ? 0f
                        : 1f - Mathf.Abs(d - (outer + inner) * 0.5f) / ((outer - inner) * 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            return _ringSprite;
        }

        /// <summary>软圆点（浮尘）。</summary>
        static Sprite Dot()
        {
            if (_dotSprite != null) return _dotSprite;
            const int s = 32;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
            tex.Apply();
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            return _dotSprite;
        }

        static SpriteRenderer Quad(Transform parent, string name, Sprite sprite, Color color,
                                   int order, float width, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            if (width > 0f && height > 0f)
            {
                var size = sprite.bounds.size;
                go.transform.localScale = new Vector3(width / size.x, height / size.y, 1f);
            }
            return sr;
        }

        static void Contain(SpriteRenderer sr, float w, float h)
        {
            var size = sr.sprite.bounds.size;
            if (size.x <= 1e-4f || size.y <= 1e-4f) return;
            sr.transform.localScale = Vector3.one * Mathf.Min(w / size.x, h / size.y);
        }

        static SpriteRenderer[] Append(SpriteRenderer[] array, SpriteRenderer item)
        {
            int n = array?.Length ?? 0;
            var next = new SpriteRenderer[n + 1];
            for (int i = 0; i < n; i++) next[i] = array[i];
            next[n] = item;
            return next;
        }

        static Color Fade(Color c, float a) => new(c.r, c.g, c.b, a);

        static void SetAlpha(SpriteRenderer sr, float a)
        {
            if (sr != null) sr.color = Fade(sr.color, a);
        }
    }
}
