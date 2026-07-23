using ClientBattle.Events;
using ClientBattle.Placeholder;
using ClientBattle.VFX;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 武将卡牌 GameObject（带子物体）：
    //   Frame（Antique 竖框）── Portrait（立绘叠在内窗上；框图中心不透明故不能放框后）
    //   ── NameLabel ── HpBar ── StatusIconPanel ── PetrifyOverlay ── BubbleAnchor
    // 立绘回退：Resources/ClientBattle/Portraits/<template_id>.png → 阵营色占位。
    // 卡框：Resources/ClientBattle/CardFrames/antique_frame（doc view 1024×1680）。
    // =========================================================================

    public class UnitView : MonoBehaviour
    {
        public HeroSnapshot Hero { get; private set; }
        public string TeamId { get; private set; }
        /// <summary>出场固定中心（槽位锚点，整局不变）。</summary>
        public Vector3 HomePosition { get; private set; }
        /// <summary>当前休息点：每次位移回位后重采样，落在 Home 为中心、边长=卡宽/4 的正方形内。</summary>
        public Vector3 RestPosition { get; private set; }
        public StatusIconPanel StatusPanel { get; private set; }
        public Transform BubbleAnchor { get; private set; }

        public int CurrentTroops { get; private set; }
        public bool Defeated { get; private set; }

        SpriteRenderer _frame, _portrait, _hpFill, _petrifyOverlay;
        TextMesh _nameLabel, _hpLabel;
        Color _frameColor;
        const float HpBarWidth = 1.5f;
        float _idlePhase; // 待机呼吸相位（按卡错开，不同步摆动）
        float _portraitBaseY;
        bool _ornateFrame; // Antique 等真框图：不染色；立绘叠内窗
        bool _aresRage;
        float _aresRageStrength = 1f;
        Coroutine _aresRagePulse;

        // All In 1：石化 / 圣盾作用在 Frame+Portrait 材质上
        Material _fxFrameMat, _fxPortraitMat;
        bool _petrified, _aegisAura;
        Coroutine _aegisPulse;

        // ---- 势能表现（B2）：四轨迷你条 + 满档常驻流光 + 溢出白闪 ----
        readonly System.Collections.Generic.Dictionary<string, SpriteRenderer> _momentumBars = new();
        readonly System.Collections.Generic.List<Color> _momentumFullTints = new();
        SpriteRenderer _momentumGlow, _overflowFlash;
        const float MomentumBarWidth = 0.34f;

        /// <summary>势能火（CFXR3）生命周期唯一管理者（挂/灭/渐灭/hold-off 全在其内）。</summary>
        public MomentumFireController MomentumFire { get; private set; }

        public static UnitView Create(HeroSnapshot hero, string teamId, Color factionColor, Vector3 position)
        {
            var go = new GameObject($"unit_{hero.HeroId}");
            var view = go.AddComponent<UnitView>();
            view.Build(hero, teamId, factionColor, position);
            return view;
        }

        void Build(HeroSnapshot hero, string teamId, Color factionColor, Vector3 position)
        {
            Hero = hero;
            TeamId = teamId;
            HomePosition = position;
            RestPosition = position;
            CurrentTroops = hero.InitialTroops;
            _frameColor = factionColor;
            transform.position = position;

            // 卡框先铺底（Antique 中心是实心暗底，非透明挖空）
            var antique = PlaceholderFactory.TryLoadSprite("CardFrames", "antique_frame");
            _ornateFrame = antique != null;
            _frame = NewSprite("Frame",
                antique != null ? antique : PlaceholderFactory.MakeSolidSprite(factionColor, 96), 0);
            if (_ornateFrame)
            {
                _frame.color = Color.white;
                FitSpriteToSlot(_frame, FrameSlotW, FrameSlotH);
            }
            else
            {
                _frame.color = factionColor;
                StretchSpriteToSlot(_frame, FrameSlotW, FrameSlotH);
            }

            // 立绘叠在内窗上（order 高于框），等比 contain 不溢出边饰
            var portraitSprite = PlaceholderFactory.GetSprite(
                "Portraits", hero.TemplateId, Color.Lerp(factionColor, Color.black, 0.35f), 96);
            _portrait = NewSprite("Portrait", portraitSprite, 1);
            FitSpriteToSlot(_portrait, PortraitSlotW, PortraitSlotH);
            _portraitBaseY = PortraitLocalY;
            _portrait.transform.localPosition = new Vector3(0f, _portraitBaseY, -0.02f);

            // 名字（框下方）
            _nameLabel = NewText("NameLabel", hero.HeroId, 42, Color.white,
                new Vector3(0f, -1.15f, 0f), 3);

            // 血条
            NewSprite("HpBack", PlaceholderFactory.MakeSolidSprite(new Color(0.12f, 0.12f, 0.12f), 8), 3)
                .transform.SetLocalPositionAndScale(new Vector3(0f, -1.38f, 0f), new Vector3(HpBarWidth, 0.14f, 1f));
            _hpFill = NewSprite("HpFill", PlaceholderFactory.MakeSolidSprite(new Color(0.35f, 0.9f, 0.4f), 8), 4);
            _hpFill.transform.SetLocalPositionAndScale(new Vector3(0f, -1.38f, 0f), new Vector3(HpBarWidth, 0.14f, 1f));
            _hpLabel = NewText("HpLabel", CurrentTroops.ToString(), 30, new Color(0.8f, 0.8f, 0.82f),
                new Vector3(0f, -1.56f, 0f), 5);

            // 石化覆盖层（默认隐藏）
            _petrifyOverlay = NewSprite("PetrifyOverlay",
                PlaceholderFactory.GetSprite("CardFrames", "petrify", new Color(0.55f, 0.55f, 0.5f, 0.85f), 96), 6);
            StretchSpriteToSlot(_petrifyOverlay, FrameSlotW, FrameSlotH);
            _petrifyOverlay.gameObject.SetActive(false);

            // 状态图标面板（卡顶外侧横排；尺寸跟卡宽）
            var panelGo = new GameObject("StatusIconPanel");
            panelGo.transform.SetParent(transform, false);
            StatusPanel = panelGo.AddComponent<StatusIconPanel>();
            StatusPanel.Configure(FrameSlotW, FrameSlotH * 0.5f);

            // 台词气泡锚点（卡牌右上）
            var anchor = new GameObject("BubbleAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(0.75f, 1.45f, 0f);
            BubbleAnchor = anchor.transform;

            // 势能常驻流光（满档轨 rim；默认隐藏）
            _momentumGlow = NewSprite("MomentumGlow",
                PlaceholderFactory.MakeSolidSprite(Color.white, 96), -1);
            StretchSpriteToSlot(_momentumGlow, FrameSlotW * 1.09f, FrameSlotH * 1.07f);
            _momentumGlow.gameObject.SetActive(false);

            // 溢出爆发白闪
            _overflowFlash = NewSprite("OverflowFlash",
                PlaceholderFactory.MakeSolidSprite(Color.white, 96), 7);
            StretchSpriteToSlot(_overflowFlash, FrameSlotW, FrameSlotH);
            _overflowFlash.gameObject.SetActive(false);

            // 四轨势能迷你条（HP 数字下方）
            int trackCount = MomentumService.TrackTable.Count;
            foreach (var style in MomentumService.TrackTable.Values)
            {
                float x = (style.Order - (trackCount - 1) / 2f) * (MomentumBarWidth + 0.05f);
                NewSprite($"MomentumBack_{style.Track}",
                        PlaceholderFactory.MakeSolidSprite(new Color(0.1f, 0.1f, 0.1f, 0.8f), 8), 3)
                    .transform.SetLocalPositionAndScale(
                        new Vector3(x, -1.72f, 0f), new Vector3(MomentumBarWidth, 0.07f, 1f));
                var fill = NewSprite($"MomentumFill_{style.Track}",
                    PlaceholderFactory.MakeSolidSprite(Color.white, 8), 4);
                fill.color = style.Tint;
                fill.transform.SetLocalPositionAndScale(
                    new Vector3(x, -1.72f, 0f), new Vector3(0f, 0.07f, 1f));
                _momentumBars[style.Track] = fill;
            }

            MomentumFire = new MomentumFireController(transform);
            _idlePhase = (position.x * 0.73f + position.y * 1.31f) * 2.4f;
        }

        void Update()
        {
            // 待机呼吸：立绘轻微上下浮动。石化/阵亡时冻结，形成静止观感。
            if (Defeated || _portrait == null) return;
            if (_petrified)
            {
                var fp = _portrait.transform.localPosition;
                _portrait.transform.localPosition = new Vector3(fp.x, _portraitBaseY, fp.z);
                return;
            }
            float bob = Mathf.Sin(Time.time * 2.1f + _idlePhase) * 0.035f;
            var p = _portrait.transform.localPosition;
            _portrait.transform.localPosition = new Vector3(p.x, _portraitBaseY + bob, p.z);

            // 满档常驻流光：alpha 呼吸脉动（Update 驱动，零 alloc）
            if (_momentumGlow != null && _momentumGlow.gameObject.activeSelf)
            {
                var c = _momentumGlow.color;
                c.a = 0.35f + Mathf.PingPong(Time.time * 0.9f, 0.3f);
                _momentumGlow.color = c;
            }
        }

        // ---------------------------------------------------------- 势能表现（B2）

        /// <summary>刷新某轨势能迷你条（value 为事件权威值；按轨类型累计）。
        /// 分档：0~3 半亮 / ≥Flash(4) 全亮 / ≥Full(5) 常驻流光（叠混各满档轨 tint）。</summary>
        public void SetMomentum(string track, int value)
        {
            if (!_momentumBars.TryGetValue(track, out var fill)) return;
            var style = MomentumService.TrackTable[track];
            float ratio = Mathf.Clamp01(value / (float)MomentumService.Full);
            fill.transform.localScale = new Vector3(MomentumBarWidth * ratio, 0.07f, 1f);
            fill.transform.localPosition = new Vector3(
                BarCenterX(style.Order) - MomentumBarWidth * (1f - ratio) / 2f, -1.72f, 0f);
            fill.color = value >= MomentumService.Flash ? style.Tint
                : new Color(style.Tint.r, style.Tint.g, style.Tint.b, 0.55f);
            if (value >= MomentumService.Full && !_momentumFullTints.Contains(style.Tint))
            {
                _momentumFullTints.Add(style.Tint);
                RefreshGlow();
            }
        }

        /// <summary>该轨首次跨过闪光档（4）的爆发帧：白闪 + 卡牌 punch 缩放（定稿乙案）。
        /// 不依赖独立 overflow prefab；日后若要用 Vefects 补一发共用 burst，在表演层追加即可。</summary>
        public void PlayMomentumOverflow(Color tint)
        {
            _overflowFlash.gameObject.SetActive(true);
            _overflowFlash.color = new Color(1f, 1f, 1f, 0.85f);
            DOTween.To(() => _overflowFlash.color, c => _overflowFlash.color = c,
                    new Color(tint.r, tint.g, tint.b, 0f), 0.45f)
                .SetLink(gameObject)
                .OnComplete(() => _overflowFlash.gameObject.SetActive(false));
            transform.DOPunchScale(Vector3.one * 0.08f, 0.3f, 6).SetLink(gameObject);
        }

        /// <summary>行动窗清零：四轨条归零、流光撤除、势能火撤除；解除火 hold-off。</summary>
        public void ClearMomentum()
        {
            foreach (var pair in _momentumBars)
            {
                var style = MomentumService.TrackTable[pair.Key];
                pair.Value.transform.localScale = new Vector3(0f, 0.07f, 1f);
                pair.Value.transform.localPosition = new Vector3(
                    BarCenterX(style.Order) - MomentumBarWidth / 2f, -1.34f, 0f);
            }
            _momentumFullTints.Clear();
            RefreshGlow();
            MomentumFire.Clear();
        }

        /// <summary>按四轨最高势能挂/调 CFXR3 火（转发控制器；MomentumService 落账驱动）。</summary>
        public void RefreshMomentumFire(int maxTrackValue) => MomentumFire.Refresh(maxTrackValue);

        // ---------------------------------------------------------- 头像标（B5 皇卡）

        SpriteRenderer _portraitMark;
        Tween _portraitMarkTween;

        /// <summary>头顶短暂浮现指定武将头像（C1：宙斯落雷/哈迪斯吸统标记）。
        /// 复用立绘三级回退（Portraits/&lt;template_id&gt;.png → 阵营色占位）。
        /// sortingOrder 高于 VFX 池默认 40，避免被落雷粒子盖住。</summary>
        public void ShowPortraitMark(string templateId, float duration = 1.4f)
        {
            if (_portraitMark == null)
            {
                _portraitMark = NewSprite("PortraitMark", null, 55);
                // 卡顶正中偏上，避开落雷中心（约 y=0.55）
                _portraitMark.transform.localPosition = new Vector3(0f, 1.55f, -0.6f);
            }
            _portraitMarkTween?.Kill();
            _portraitMark.sprite = PlaceholderFactory.GetSprite(
                "Portraits", templateId, new Color(0.5f, 0.4f, 0.6f), 96);
            FitSpriteToSlot(_portraitMark, PortraitMarkSlot, PortraitMarkSlot);
            _portraitMark.sortingOrder = 55;
            _portraitMark.gameObject.SetActive(true);
            _portraitMark.color = new Color(1f, 1f, 1f, 0f);
            var seq = DOTween.Sequence().SetLink(gameObject);
            seq.Append(DOTween.To(() => _portraitMark.color, c => _portraitMark.color = c,
                Color.white, 0.08f));
            seq.AppendInterval(duration);
            seq.Append(DOTween.To(() => _portraitMark.color, c => _portraitMark.color = c,
                new Color(1f, 1f, 1f, 0f), 0.25f));
            seq.OnComplete(() => _portraitMark.gameObject.SetActive(false));
            _portraitMarkTween = seq;
        }

        SpriteRenderer _overlayFlashIcon;
        Tween _overlayFlashTween;

        /// <summary>卡面中央短暂闪图标（普通格挡 / 圣盾反伤等）；淡入→持→淡出。
        /// 资源 Resources/ClientBattle/VFX/&lt;key&gt;.png，无则色块占位。</summary>
        public void FlashOverlayIcon(string iconKey, Color? tint = null, float duration = 0.7f)
        {
            if (string.IsNullOrEmpty(iconKey)) return;
            if (_overlayFlashIcon == null)
            {
                _overlayFlashIcon = NewSprite("OverlayFlashIcon", null, 32);
                _overlayFlashIcon.transform.localPosition = new Vector3(0f, 0.2f, -0.55f);
            }
            _overlayFlashTween?.Kill();
            var color = tint ?? Color.white;
            _overlayFlashIcon.sprite = PlaceholderFactory.GetSprite(
                "VFX", iconKey, color, 64);
            FitSpriteToSlot(_overlayFlashIcon, FrameSlotW * 0.55f, FrameSlotW * 0.55f);
            _overlayFlashIcon.sortingOrder = 32;
            _overlayFlashIcon.gameObject.SetActive(true);
            _overlayFlashIcon.color = new Color(color.r, color.g, color.b, 0f);
            float fadeIn = Mathf.Min(0.14f, duration * 0.22f);
            float fadeOut = Mathf.Min(0.28f, duration * 0.4f);
            float hold = Mathf.Max(0.08f, duration - fadeIn - fadeOut);
            var seq = DOTween.Sequence().SetLink(gameObject);
            seq.Append(DOTween.To(() => _overlayFlashIcon.color, c => _overlayFlashIcon.color = c,
                new Color(color.r, color.g, color.b, 1f), fadeIn));
            seq.AppendInterval(hold);
            seq.Append(DOTween.To(() => _overlayFlashIcon.color, c => _overlayFlashIcon.color = c,
                new Color(color.r, color.g, color.b, 0f), fadeOut));
            seq.OnComplete(() =>
            {
                if (_overlayFlashIcon != null) _overlayFlashIcon.gameObject.SetActive(false);
            });
            _overlayFlashTween = seq;
        }

        float BarCenterX(int order) =>
            (order - (MomentumService.TrackTable.Count - 1) / 2f) * (MomentumBarWidth + 0.05f);

        void RefreshGlow()
        {
            if (_momentumFullTints.Count == 0)
            {
                _momentumGlow.gameObject.SetActive(false);
                return;
            }
            var mixed = Color.black; // 多轨满档叠色（加法混合后归一）
            foreach (var t in _momentumFullTints) mixed += t;
            mixed /= _momentumFullTints.Count;
            mixed.a = 0.5f;
            _momentumGlow.color = mixed;
            _momentumGlow.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------- 状态呈现

        /// <summary>以事件为准刷新兵力（troops_after 权威值，客户端不做减法）。</summary>
        public void SetTroops(int troopsAfter)
        {
            CurrentTroops = Mathf.Max(0, troopsAfter);
            float ratio = Hero.MaxTroops > 0 ? (float)CurrentTroops / Hero.MaxTroops : 0f;
            _hpFill.transform.localScale = new Vector3(HpBarWidth * ratio, 0.14f, 1f);
            _hpFill.transform.localPosition = new Vector3(-HpBarWidth * (1f - ratio) / 2f, -1.0f, 0f);
            _hpFill.color = ratio > 0.5f ? new Color(0.35f, 0.9f, 0.4f)
                          : ratio > 0.2f ? new Color(0.95f, 0.75f, 0.2f)
                                         : new Color(0.9f, 0.25f, 0.2f);
            _hpLabel.text = CurrentTroops.ToString();
        }

        /// <summary>在出场固定中心附近重采样休息点（正方形边长 = 卡宽/4）。</summary>
        public Vector3 RerollRestPosition()
        {
            float half = FrameSlotW / 8f; // 边长 FrameSlotW/4 → 半边 FrameSlotW/8
            RestPosition = HomePosition + new Vector3(
                Random.Range(-half, half),
                Random.Range(-half, half),
                0f);
            return RestPosition;
        }

        /// <summary>位移回位：先重采样休息点，再 tween 过去（演出层观感抖动，不影响结算）。</summary>
        public Tween DOMoveReturnHome(float duration, Ease ease = Ease.OutQuad)
        {
            return transform.DOMove(RerollRestPosition(), duration)
                .SetEase(ease).SetLink(gameObject);
        }

        /// <summary>受击顿挫：短促位移抖动 + 红闪；结束后重采样休息点（同回位微抖区域）并贴回。</summary>
        public void HitReact(bool isCrit)
        {
            transform.DOKill(true);
            transform.DOShakePosition(isCrit ? 0.3f : 0.18f, isCrit ? 0.22f : 0.12f, 20)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    if (Defeated) return;
                    transform.position = RerollRestPosition();
                });
            FlashPortrait(isCrit ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.55f, 0.55f));
        }

        void FlashPortrait(Color flash)
        {
            var original = Color.white;
            _portrait.color = flash;
            _portrait.DOKill(); // 连点/连击互斥：先杀上一次立绘闪
            DOTween.To(() => _portrait.color, c => _portrait.color = c, original, 0.25f)
                .SetTarget(_portrait).SetLink(gameObject);
        }

        Tween _petrifyTween;

        /// <summary>石化：All In 1 灰阶石色渐变；并冻结一切呼吸/浮动，形成静止像。
        /// 施加/解除的渐变互斥：新调用先杀旧 tween。</summary>
        public void SetPetrified(bool on)
        {
            _petrifyTween?.Kill();
            _petrifyTween = null;
            _petrified = on;
            SetBreathingFrozen(on);
            if (EnsureAllIn1Mats())
            {
                // 旧覆盖层关掉
                if (_petrifyOverlay != null) _petrifyOverlay.gameObject.SetActive(false);
                RefreshAllIn1Keywords();
                if (on)
                {
                    AllIn1CardFx.SetPetrifyAmount(_fxFrameMat, _fxPortraitMat, 0f);
                    _petrifyTween = DOVirtual.Float(0f, 1f, 0.4f, v =>
                        AllIn1CardFx.SetPetrifyAmount(_fxFrameMat, _fxPortraitMat, v))
                        .SetLink(gameObject);
                }
                else
                {
                    float start = _fxPortraitMat != null
                        ? _fxPortraitMat.GetFloat("_GreyscaleBlend") / AllIn1CardFx.PetrifyPortraitMax
                        : 1f;
                    start = Mathf.Clamp01(start);
                    _petrifyTween = DOVirtual.Float(start, 0f, 0.5f, v =>
                        AllIn1CardFx.SetPetrifyAmount(_fxFrameMat, _fxPortraitMat, v))
                        .SetLink(gameObject)
                        .OnComplete(RefreshAllIn1Keywords);
                }
                return;
            }

            // 回退：旧灰色覆盖层
            if (on)
            {
                _petrifyOverlay.gameObject.SetActive(true);
                _petrifyOverlay.color = new Color(1f, 1f, 1f, 0f);
                _petrifyTween = DOTween.To(
                    () => _petrifyOverlay.color, c => _petrifyOverlay.color = c, Color.white, 0.3f)
                    .SetLink(gameObject);
            }
            else if (_petrifyOverlay.gameObject.activeSelf)
            {
                _petrifyTween = DOTween.To(() => _petrifyOverlay.color, c => _petrifyOverlay.color = c,
                        new Color(1f, 1f, 1f, 0f), 0.5f)
                    .SetLink(gameObject)
                    .OnComplete(() => _petrifyOverlay.gameObject.SetActive(false));
            }
        }

        /// <summary>石化冻结：立绘归位、怒火/圣盾呼吸停、雷霆驱动关、光环粒子暂停。</summary>
        void SetBreathingFrozen(bool frozen)
        {
            if (_portrait != null)
            {
                var p = _portrait.transform.localPosition;
                _portrait.transform.localPosition = new Vector3(p.x, _portraitBaseY, p.z);
            }

            if (_momentumGlow != null && _momentumGlow.gameObject.activeSelf)
            {
                var c = _momentumGlow.color;
                c.a = frozen ? 0.4f : c.a;
                _momentumGlow.color = c;
            }

            // 阿瑞斯红呼吸
            if (_aresRagePulse != null)
            {
                StopCoroutine(_aresRagePulse);
                _aresRagePulse = null;
            }
            if (frozen)
                ApplyFrameRestColor();
            else if (_aresRage && !Defeated)
                _aresRagePulse = StartCoroutine(AresRagePulseLoop());

            // 圣盾描边呼吸
            if (_aegisPulse != null)
            {
                StopCoroutine(_aegisPulse);
                _aegisPulse = null;
            }
            if (!frozen && _aegisAura && _fxFrameMat != null)
                _aegisPulse = StartCoroutine(AegisPulseLoop());

            foreach (var driver in GetComponentsInChildren<ThunderAuraDriver>(true))
                driver.enabled = !frozen;

            foreach (Transform child in transform)
            {
                if (child == null || !child.name.StartsWith("AuraMount")) continue;
                foreach (var ps in child.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (frozen) ps.Pause(true);
                    else if (ps.isPaused) ps.Play(true);
                }
            }
        }

        /// <summary>圣盾常驻：All In 1 卡框金色描边（无整卡 Glow）。</summary>
        public void SetAegisAura(bool on)
        {
            _aegisAura = on;
            if (!EnsureAllIn1Mats()) return;
            RefreshAllIn1Keywords();
            if (_aegisPulse != null)
            {
                StopCoroutine(_aegisPulse);
                _aegisPulse = null;
            }
            if (on && !_petrified) _aegisPulse = StartCoroutine(AegisPulseLoop());
        }

        /// <summary>阿瑞斯怒火：卡框红光呼吸（strength 0.55=血战 / 1=战神之勇）。</summary>
        public void SetAresRage(bool on, float strength = 1f)
        {
            _aresRage = on;
            _aresRageStrength = Mathf.Clamp01(strength);
            if (_aresRagePulse != null)
            {
                StopCoroutine(_aresRagePulse);
                _aresRagePulse = null;
            }
            if (on && !Defeated && !_petrified)
                _aresRagePulse = StartCoroutine(AresRagePulseLoop());
            else if (!on || _petrified)
                ApplyFrameRestColor();
        }

        Color FrameRestColor() => _ornateFrame ? Color.white : _frameColor;

        void ApplyFrameRestColor()
        {
            if (_frame != null) _frame.color = ApplyDim(FrameRestColor());
        }

        System.Collections.IEnumerator AresRagePulseLoop()
        {
            // 战神之勇：峰值大红；其他（血战等）：峰值仅微红
            bool mighty = _aresRageStrength >= 0.9f;
            float period = mighty ? 1.05f : 1.55f;
            var rest = FrameRestColor();
            var hot = mighty
                ? Color.Lerp(rest, new Color(1f, 0.12f, 0.08f), 0.9f)
                : Color.Lerp(rest, new Color(1f, 0.45f, 0.4f), 0.22f);
            while (_aresRage && !_petrified && !Defeated && _frame != null)
            {
                float t = 0f;
                while (t < period && _aresRage && !_petrified && !Defeated)
                {
                    t += Time.deltaTime;
                    float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / period);
                    _frame.color = ApplyDim(Color.Lerp(rest, hot, wave));
                    yield return null;
                }
            }
            if (!_aresRage || _petrified) ApplyFrameRestColor();
        }

        bool EnsureAllIn1Mats()
        {
            if (_fxFrameMat != null && _fxPortraitMat != null) return true;
            var frameMat = AllIn1CardFx.CreateFxMaterial();
            var portraitMat = AllIn1CardFx.CreateFxMaterial();
            if (frameMat == null || portraitMat == null) return false;
            _fxFrameMat = frameMat;
            _fxPortraitMat = portraitMat;
            _frame.material = _fxFrameMat;
            _portrait.material = _fxPortraitMat;
            // 保留阵营框染色：走 SpriteRenderer.color，材质 _Color 保持白
            return true;
        }

        void RefreshAllIn1Keywords()
        {
            AllIn1CardFx.Apply(_fxFrameMat, _petrified, _aegisAura, isFrame: true);
            AllIn1CardFx.Apply(_fxPortraitMat, _petrified, _aegisAura, isFrame: false);
        }

        System.Collections.IEnumerator AegisPulseLoop()
        {
            // 只轻微呼吸描边亮度，绝不改整卡 Glow
            while (_aegisAura && !_petrified && _fxFrameMat != null)
            {
                float t = 0f;
                while (t < 1.4f && _aegisAura && !_petrified)
                {
                    t += Time.deltaTime;
                    float wave = 0.75f + 0.25f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f / 1.4f));
                    _fxFrameMat.SetFloat("_OutlineAlpha", 0.7f * wave + 0.15f);
                    _fxFrameMat.SetFloat("_OutlineGlow", 1.35f + 0.4f * wave);
                    yield return null;
                }
            }
        }

        Tween _dimTween;
        float _dimAmount; // 0=正常 1=压暗（单挑无关武将）
        public const float DimFadeSeconds = 0.45f;
        /// <summary>压暗目标亮度倍率（微微发灰，勿过黑）。</summary>
        const float SoftDimMul = 0.78f;

        /// <summary>压暗（单挑聚焦：全场非对阵双方微微发灰）。渐变；怒火呼吸也乘算本倍率。</summary>
        public void SetDimmed(bool dimmed, float duration = DimFadeSeconds)
        {
            if (Defeated) return; // 阵亡已灰化，不参与单挑压暗
            _dimTween?.Kill();
            float from = _dimAmount;
            float to = dimmed ? 1f : 0f;

            if (duration <= 0.001f)
            {
                _dimAmount = to;
                ApplyDimVisuals();
                return;
            }

            _dimTween = DOTween.To(() => from, v =>
            {
                _dimAmount = v;
                ApplyDimVisuals();
            }, to, duration).SetEase(Ease.InOutQuad).SetLink(gameObject);
        }

        /// <summary>当前压暗乘子（怒火脉冲等动态着色时调用）。</summary>
        public Color ApplyDim(Color c)
        {
            if (_dimAmount <= 0.001f) return c;
            float m = Mathf.Lerp(1f, SoftDimMul, _dimAmount);
            return new Color(c.r * m, c.g * m, c.b * m, c.a);
        }

        void ApplyDimVisuals()
        {
            if (Defeated) return;
            float m = Mathf.Lerp(1f, SoftDimMul, _dimAmount);
            var tint = new Color(m, m, m, 1f);

            if (_portrait != null) _portrait.color = tint;
            // 怒火脉冲每帧写框色，此处只在无怒火时写框；有怒火由脉冲走 ApplyDim
            if (!_aresRage && _frame != null)
                _frame.color = ApplyDim(FrameRestColor());

            if (_nameLabel != null)
                _nameLabel.color = new Color(m, m, m, 1f);
            if (_hpFill != null)
                _hpFill.color = new Color(0.35f * m, 0.9f * m, 0.4f * m, 1f);
            if (_hpLabel != null)
                _hpLabel.color = new Color(0.8f * m, 0.8f * m, 0.82f * m, 1f);

            foreach (var kv in _momentumBars)
            {
                if (kv.Value == null) continue;
                if (MomentumService.TrackTable.TryGetValue(kv.Key, out var style))
                    kv.Value.color = ApplyDim(style.Tint);
            }
        }

        /// <summary>阵亡：变灰倒下（保留尸位，主将阵亡由横幅另行强调）。</summary>
        public void PlayDefeated()
        {
            Defeated = true;
            transform.DOKill();
            _dimTween?.Kill();
            SetAresRage(false);
            _portrait.color = new Color(0.35f, 0.35f, 0.35f);
            _frame.color = _ornateFrame
                ? new Color(0.4f, 0.4f, 0.42f)
                : new Color(0.3f, 0.3f, 0.3f);
            transform.DORotate(new Vector3(0f, 0f, TeamId == "A" ? -80f : 80f), 0.45f)
                .SetEase(Ease.InQuad).SetLink(gameObject);
            transform.DOMove(RestPosition + new Vector3(0f, -0.25f, 0f), 0.45f)
                .SetLink(gameObject);
        }

        /// <summary>整局重置（下一局开始时复活重摆）。</summary>
        public void ResetForNewGame(int initialTroops)
        {
            Defeated = false;
            transform.DOKill();
            _dimTween?.Kill();
            _dimAmount = 0f;
            RestPosition = HomePosition;
            transform.position = HomePosition;
            transform.rotation = Quaternion.identity;
            _portrait.color = Color.white;
            _frame.color = _ornateFrame ? Color.white : _frameColor;
            if (_nameLabel != null) _nameLabel.color = Color.white;
            if (_hpFill != null) _hpFill.color = new Color(0.35f, 0.9f, 0.4f);
            if (_hpLabel != null) _hpLabel.color = new Color(0.8f, 0.8f, 0.82f);
            SetPetrified(false);
            SetAresRage(false);
            SetTroops(initialTroops);
            StatusPanel.Clear();
            ClearMomentum();
        }

        // ---------------------------------------------------------- 工具

        // 卡面槽位：Antique doc view 1024×1680 → 外框 1.55×2.54；内窗约 62%×58%
        const float FrameSlotW = 1.55f;
        const float FrameSlotH = 2.54f;
        const float PortraitSlotW = 0.96f;
        const float PortraitSlotH = 1.47f;
        const float PortraitLocalY = 0.06f; // 顶部饰件略高，立绘微下移居中内窗
        const float PortraitMarkSlot = 0.72f;

        /// <summary>按 sprite.bounds 等比缩放到槽内（contain，不拉伸）。
        /// 与 BackgroundFitter 的 cover 同族；立绘用 contain 避免裁切无遮罩时溢出。</summary>
        static void FitSpriteToSlot(SpriteRenderer sr, float slotW, float slotH)
        {
            if (sr == null || sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            if (size.x < 1e-4f || size.y < 1e-4f) return;
            float s = Mathf.Min(slotW / size.x, slotH / size.y);
            sr.transform.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>铺满槽位（可非等比）。卡框/石化层/闪光用：占位方块需拉成卡面比例。</summary>
        static void StretchSpriteToSlot(SpriteRenderer sr, float slotW, float slotH)
        {
            if (sr == null || sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            if (size.x < 1e-4f || size.y < 1e-4f) return;
            sr.transform.localScale = new Vector3(slotW / size.x, slotH / size.y, 1f);
        }

        SpriteRenderer NewSprite(string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            return renderer;
        }

        TextMesh NewText(string name, string text, int fontSize, Color color, Vector3 localPos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.08f;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = fontSize;
            mesh.color = color;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sortingOrder = order + 10;
            return mesh;
        }
    }

    static class TransformExt
    {
        public static void SetLocalPositionAndScale(this Transform t, Vector3 pos, Vector3 scale)
        {
            t.localPosition = pos;
            t.localScale = scale;
        }
    }
}
