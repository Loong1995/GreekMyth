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
        /// <summary>出场固定中心 = 逻辑格心（原站位点，整局不变）。</summary>
        public Vector3 HomePosition { get; private set; }
        /// <summary>休息点 = 以 Home 为圆心、半径卡宽/6 的圆盘内均匀随机点
        /// （下缘贴该点；建卡与回位重采样同源）。</summary>
        public Vector3 RestPosition { get; private set; }
        float _layoutScale = 1f;
        float _frameW = StanceLayout.RefFrameWFallback;
        float _frameH = StanceLayout.RefFrameHFallback;
        float _portraitW, _portraitH, _portraitLocalY, _portraitMarkSlot;
        float _hpBarWidth, _momentumBarWidth;
        public StatusIconPanel StatusPanel { get; private set; }
        public Transform BubbleAnchor { get; private set; }

        public int CurrentTroops { get; private set; }
        public bool Defeated { get; private set; }

        SpriteRenderer _frame, _portrait, _hpFill, _petrifyOverlay;

        /// <summary>残影快照源：卡框与立绘。只读，供 <see cref="AfterImageService"/>
        /// 拷贝**当前**姿态与颜色（含压暗/石化/染色），故不能预制成 prefab。</summary>
        internal SpriteRenderer FrameRenderer => _frame;
        internal SpriteRenderer PortraitRenderer => _portrait;

        /// <summary>藏起/还原卡面立绘。单挑 cut-in 期间立绘由飞行替身代演，
        /// 卡上不能同时还留着同一张图（否则出框读作"复制"而不是"拽出来"）。
        ///
        /// 只动可见性，不碰颜色/位置——压暗、石化、待机浮动这些状态在藏起期间
        /// 照常写入，还原时立绘直接接上当时的状态，不会闪一下旧样子。
        /// **谁藏谁还**：藏起方必须在正常收尾与中断两条路径上都还原
        /// （见 CutInService.CancelAll）。</summary>
        public void SetPortraitHidden(bool hidden)
        {
            if (_portrait != null) _portrait.gameObject.SetActive(!hidden);
        }
        TextMesh _nameLabel, _hpLabel;
        Color _frameColor;
        float _idlePhase; // 待机相位（按卡错开，六张卡不同步摆动）
        float _portraitBaseY;
        /// <summary>卡面生动性（呼吸/惯性视差/受击挤压）唯一写入者，见 CardIdleMotion。
        /// 任何要动立绘 Transform 的新表现都必须走它，别再另起 tween 抢同一个组件。</summary>
        readonly CardIdleMotion _idleMotion = new();

        /// <summary>本卡的卡姿基准（含每卡随机后倾角）。受击摆动叠在它之上。</summary>
        Quaternion _baseLean = Quaternion.identity;
        bool _ornateFrame; // Antique 等真框图：不染色；立绘叠内窗
        bool _aresRage;
        float _aresRageStrength = 1f;
        Coroutine _aresRagePulse;

        // All In 1：石化 / 圣盾作用在 Frame+Portrait 材质上
        Material _fxFrameMat, _fxPortraitMat;
        bool _petrified, _aegisAura;
        Coroutine _aegisPulse;

        // ---- 势能表现（B2）：四轨迷你条 + 溢出白闪（火/金光环见 MomentumFireController）----
        readonly System.Collections.Generic.Dictionary<string, SpriteRenderer> _momentumBars = new();
        SpriteRenderer _overflowFlash;

        /// <summary>势能火（CFXR3）生命周期唯一管理者（挂/灭/渐灭/hold-off 全在其内）。</summary>
        public MomentumFireController MomentumFire { get; private set; }

        public static UnitView Create(HeroSnapshot hero, string teamId, Color factionColor,
            Vector3 position, float restJitterHalf = -1f)
        {
            var go = new GameObject($"unit_{hero.HeroId}");
            var view = go.AddComponent<UnitView>();
            view.Build(hero, teamId, factionColor, position, restJitterHalf);
            return view;
        }

        void ApplyLayoutMetrics(float restJitterHalf)
        {
            // 尺寸已由 BattleBoardView 在 Fit 相机后 RecalcFromCamera；此处只读结果
            if (StanceLayout.CardHeight < 0.2f)
                StanceLayout.RecalcFromCamera(Camera.main);
            _frameW = StanceLayout.CardWidth;
            _frameH = StanceLayout.CardHeight;
            _layoutScale = StanceLayout.LayoutScale;
            _ = restJitterHalf; // 抖动半径改由 SlotJitterRadius 权威，参数保留兼容调用方
            _portraitW = _frameW * (0.96f / StanceLayout.RefFrameW);
            _portraitH = _frameH * (1.47f / StanceLayout.RefFrameH);
            _portraitLocalY = 0.06f * _layoutScale;
            _portraitMarkSlot = 0.72f * _layoutScale;
            _hpBarWidth = 1.5f * _layoutScale;
            _momentumBarWidth = 0.34f * _layoutScale;
        }

        float S(float v) => v * _layoutScale;

        /// <summary>近 3D 卡姿：绕 X 后倾。基准角＝`CameraFitter.CardPitchDeg`
        /// （唯一真源，**不读相机**——角度链见该处注释），每卡再在
        /// **基准 ± `StagePerformanceConfig.CardPitchJitterDeg`** 内随机一个
        /// 自己的角度（现 45±5 ＝ 40°~50°）：六张卡的倾角略有参差，
        /// 整排才不像同一块板刷出来的。
        ///
        /// **只抖视觉**：`GroundPoint` / `GroundFoot` / `CardShadowDepth` 等几何
        /// 一律仍按基准角算，否则站位落点与影子会跟着每卡的随机角一起漂
        /// （几度之内目视无差，故此近似成立；抖动幅度别开太大）。</summary>
        void ApplyCardLean()
        {
            if (!CameraFitter.PerspectivePilot)
            {
                _baseLean = Quaternion.identity;
            }
            else
            {
                float jitter = Mathf.Max(0f, StagePerformanceConfig.CardPitchJitterDeg);
                _baseLean = Quaternion.Euler(
                    CameraFitter.CardPitchDeg + Random.Range(-jitter, jitter), 0f, 0f);
            }
            CancelHitTremble();
            transform.rotation = _baseLean;
        }

        // ------------------------------------------------- 微调圆（站位活动上限）
        //
        // 受击击退与出击后的前进休息点**共用**这一个圆：卡牌只在自己的圆盘里
        // 一进一退地游走，永不会越打越偏。半径默认与原站位微抖圆重合。
        // （旧名"击打圆"，2026-07-27 更名：它管的是站位微调区域，不只受击。）

        static bool Grounded => CameraFitter.PerspectivePilot && ArenaSlotLayout.GroundActive;

        static float TuneCircleRadius =>
            StanceLayout.SlotJitterRadius * Mathf.Max(0f, StagePerformanceConfig.TuneCircleScale);

        /// <summary>卡牌锚点 → 相对 Home 的**地面平面**偏移（透视取地面 XZ，正交取世界 XY）。
        /// 微调圆的一切裁剪都在这套二维坐标里做——近 3D 下直接用世界向量
        /// 会把纵深错算成高度。</summary>
        Vector2 OffsetFromHome(Vector3 cardAnchor)
        {
            if (Grounded)
            {
                var home = ArenaSlotLayout.GroundFoot(HomePosition);
                var at = ArenaSlotLayout.GroundFoot(cardAnchor);
                return new Vector2(at.x - home.x, at.z - home.z);
            }
            return new Vector2(cardAnchor.x - HomePosition.x, cardAnchor.y - HomePosition.y);
        }

        /// <summary><see cref="OffsetFromHome"/> 的逆：地面平面偏移 → 卡牌锚点。</summary>
        Vector3 AnchorAtOffset(Vector2 offset)
        {
            if (Grounded)
            {
                var home = ArenaSlotLayout.GroundFoot(HomePosition);
                return ArenaSlotLayout.GroundPoint(home.x + offset.x, home.z + offset.y);
            }
            return HomePosition + new Vector3(offset.x, offset.y, 0f);
        }

        /// <summary>越界即截断到微调圆边界（不是反弹、不是取模）。</summary>
        static Vector2 ClampToTuneCircle(Vector2 offset)
        {
            float r = TuneCircleRadius;
            return offset.sqrMagnitude > r * r ? offset.normalized * r : offset;
        }

        void Build(HeroSnapshot hero, string teamId, Color factionColor, Vector3 position,
            float restJitterHalf)
        {
            ApplyLayoutMetrics(restJitterHalf);
            Hero = hero;
            TeamId = teamId;
            HomePosition = position; // 逻辑格心（原站位点）
            RestPosition = SampleRestAroundHome();
            CurrentTroops = hero.InitialTroops;
            _frameColor = factionColor;
            transform.position = RestPosition;
            ApplyCardLean();

            // 卡框先铺底（Antique 中心是实心暗底，非透明挖空）
            var antique = PlaceholderFactory.TryLoadSprite("CardFrames", "antique_frame");
            _ornateFrame = antique != null;
            _frame = NewSprite("Frame",
                antique != null ? antique : PlaceholderFactory.MakeSolidSprite(factionColor, 96), 0);
            if (_ornateFrame)
            {
                _frame.color = Color.white;
                FitSpriteToSlot(_frame, _frameW, _frameH);
            }
            else
            {
                _frame.color = factionColor;
                StretchSpriteToSlot(_frame, _frameW, _frameH);
            }

            // 立绘叠在内窗上（order 高于框），等比 contain 不溢出边饰
            var portraitSprite = PlaceholderFactory.GetSprite(
                "Portraits", hero.TemplateId, Color.Lerp(factionColor, Color.black, 0.35f), 96);
            _portrait = NewSprite("Portrait", portraitSprite, 1);
            FitSpriteToSlot(_portrait, _portraitW, _portraitH);
            _portraitBaseY = _portraitLocalY;
            // 景深：立绘沿卡面法线抬离卡框。近 3D 下卡牌后倾 45°，这点间距
            // 配合惯性滞后就能读出「框里装着一个人」而非一张贴纸；
            // 抬太多会在斜视角下明显浮空，PortraitDepth 是目视上限。
            _portrait.transform.localPosition =
                new Vector3(0f, _portraitBaseY, -PortraitDepth * _layoutScale);

            // 深度代理：卡牌进入深度图与不透明贴图，厂包的折射壳/软粒子/深度排序
            // 才能正确处理卡牌（详见 CardDepthProxy）。不改卡面本体渲染。
            CardDepthProxy.AttachTo(_frame);
            CardDepthProxy.AttachTo(_portrait);

            // 接地阴影：没有接触阴影的物体一律被读作浮空贴纸（近 3D 舞台才建）
            CardGroundShadow.AttachTo(this);

            // 名字（框下方）
            _nameLabel = NewText("NameLabel", hero.HeroId, Mathf.RoundToInt(42 * _layoutScale), Color.white,
                new Vector3(0f, S(-1.15f), 0f), 3);

            // 血条
            NewSprite("HpBack", PlaceholderFactory.MakeSolidSprite(new Color(0.12f, 0.12f, 0.12f), 8), 3)
                .transform.SetLocalPositionAndScale(new Vector3(0f, S(-1.38f), 0f),
                    new Vector3(_hpBarWidth, S(0.14f), 1f));
            _hpFill = NewSprite("HpFill", PlaceholderFactory.MakeSolidSprite(new Color(0.35f, 0.9f, 0.4f), 8), 4);
            _hpFill.transform.SetLocalPositionAndScale(new Vector3(0f, S(-1.38f), 0f),
                new Vector3(_hpBarWidth, S(0.14f), 1f));
            _hpLabel = NewText("HpLabel", CurrentTroops.ToString(), Mathf.RoundToInt(30 * _layoutScale),
                new Color(0.8f, 0.8f, 0.82f), new Vector3(0f, S(-1.56f), 0f), 5);

            // 石化覆盖层（默认隐藏）
            _petrifyOverlay = NewSprite("PetrifyOverlay",
                PlaceholderFactory.GetSprite("CardFrames", "petrify", new Color(0.55f, 0.55f, 0.5f, 0.85f), 96), 6);
            StretchSpriteToSlot(_petrifyOverlay, _frameW, _frameH);
            _petrifyOverlay.gameObject.SetActive(false);

            // 状态图标面板（卡顶外侧横排；尺寸跟卡宽）
            var panelGo = new GameObject("StatusIconPanel");
            panelGo.transform.SetParent(transform, false);
            StatusPanel = panelGo.AddComponent<StatusIconPanel>();
            StatusPanel.Configure(_frameW, _frameH * 0.5f);

            // 台词气泡锚点（卡牌右上外侧，落在 LineReserve 带内）
            var anchor = new GameObject("BubbleAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(S(0.75f), S(1.45f), 0f);
            BubbleAnchor = anchor.transform;

            // 满档金光环：由 MomentumFireController 与势能火同档同灭

            // 溢出爆发白闪
            _overflowFlash = NewSprite("OverflowFlash",
                PlaceholderFactory.MakeSolidSprite(Color.white, 96), 7);
            StretchSpriteToSlot(_overflowFlash, _frameW, _frameH);
            _overflowFlash.gameObject.SetActive(false);

            // 势能迷你条已取消展示（2026-07-25）：势能表现只保留火/金光环/白闪；
            // _momentumBars 留空，SetMomentum/ClearMomentum 自然空转。

            MomentumFire = new MomentumFireController(transform);
            _idlePhase = (position.x * 0.73f + position.y * 1.31f) * 2.4f;
            _idleMotion.Bind(_portrait.transform, _layoutScale, _idlePhase);
        }

        /// <summary>立绘抬离卡框的景深（世界单位 × LayoutScale）。</summary>
        const float PortraitDepth = 0.05f;

        void Update()
        {
            // 卡面生动性统一由合成器写立绘 Transform（呼吸/惯性视差/受击挤压）；
            // 石化与阵亡由 SetFrozen 冻成静止像，此处不再各自判分支。
            if (_portrait == null) return;
            float dt = Time.deltaTime;
            TickHitTremble(dt); // 击退落定后的沿线前后颤（纯动画，围绕落点）
            float ratio = Hero != null && Hero.MaxTroops > 0
                ? CurrentTroops / (float)Hero.MaxTroops : 1f;
            _idleMotion.Tick(transform, dt, ratio);
        }

        // ---------------------------------------------------------- 势能表现（B2）

        /// <summary>刷新某轨势能迷你条（value 为事件权威值；按轨类型累计）。
        /// 分档：0~3 半亮 / ≥Flash(4) 全亮；火+金光环由 RefreshMomentumFire 驱动。</summary>
        public void SetMomentum(string track, int value)
        {
            if (!_momentumBars.TryGetValue(track, out var fill)) return;
            var style = MomentumService.TrackTable[track];
            float ratio = Mathf.Clamp01(value / (float)MomentumService.Full);
            fill.transform.localScale = new Vector3(_momentumBarWidth * ratio, S(0.07f), 1f);
            fill.transform.localPosition = new Vector3(
                BarCenterX(style.Order) - _momentumBarWidth * (1f - ratio) / 2f, S(-1.72f), 0f);
            fill.color = value >= MomentumService.Flash ? style.Tint
                : new Color(style.Tint.r, style.Tint.g, style.Tint.b, 0.55f);
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

        /// <summary>行动窗清零：四轨条归零、火+金光环撤除。</summary>
        public void ClearMomentum()
        {
            foreach (var pair in _momentumBars)
            {
                var style = MomentumService.TrackTable[pair.Key];
                pair.Value.transform.localScale = new Vector3(0f, S(0.07f), 1f);
                pair.Value.transform.localPosition = new Vector3(
                    BarCenterX(style.Order) - _momentumBarWidth / 2f, S(-1.72f), 0f);
            }
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
                _portraitMark.transform.localPosition = new Vector3(0f, S(1.55f), -0.6f);
            }
            _portraitMarkTween?.Kill();
            _portraitMark.sprite = PlaceholderFactory.GetSprite(
                "Portraits", templateId, new Color(0.5f, 0.4f, 0.6f), 96);
            FitSpriteToSlot(_portraitMark, _portraitMarkSlot, _portraitMarkSlot);
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
            FitSpriteToSlot(_overlayFlashIcon, _frameW * 0.55f, _frameW * 0.55f);
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
            (order - (MomentumService.TrackTable.Count - 1) / 2f) * (_momentumBarWidth + S(0.05f));

        // ---------------------------------------------------------- 状态呈现

        /// <summary>以事件为准刷新兵力（troops_after 权威值，客户端不做减法）。</summary>
        public void SetTroops(int troopsAfter)
        {
            CurrentTroops = Mathf.Max(0, troopsAfter);
            float ratio = Hero.MaxTroops > 0 ? (float)CurrentTroops / Hero.MaxTroops : 0f;
            _hpFill.transform.localScale = new Vector3(_hpBarWidth * ratio, S(0.14f), 1f);
            _hpFill.transform.localPosition = new Vector3(
                -_hpBarWidth * (1f - ratio) / 2f, S(-1.38f), 0f);
            _hpFill.color = ratio > 0.5f ? new Color(0.35f, 0.9f, 0.4f)
                          : ratio > 0.2f ? new Color(0.95f, 0.75f, 0.2f)
                                         : new Color(0.9f, 0.25f, 0.2f);
            _hpLabel.text = CurrentTroops.ToString();
        }

        /// <summary>在原站位点（Home）为圆心、半径卡宽/6 的圆盘内均匀采样休息点。
        /// 透视：在地面 XZ 采样后经 GroundPoint 贴下缘；正交：在 XY 平面采样。</summary>
        public Vector3 RerollRestPosition()
        {
            RestPosition = SampleRestAroundHome();
            return RestPosition;
        }

        Vector3 SampleRestAroundHome()
        {
            StanceLayout.SampleSlotDiskOffset(out float dx, out float dy);
            return AnchorAtOffset(ClampToTuneCircle(new Vector2(dx, dy)));
        }

        /// <summary>沿本次行动方向在微调圆内取一个**前进**休息点：出击后不回原位，
        /// 而是往打过去的方向落一点。与受击的向后击退互为一对——
        /// 打出去的人往前站、挨打的人被推回去，一来一回，整局站位是活的而不是钉死的。
        /// 两者都夹在微调圆内，所以位置只会在圆盘里游走，不会走失。</summary>
        public Vector3 RerollRestPositionToward(Vector3 towardWorld)
        {
            Vector2 forward = OffsetFromHome(towardWorld);
            if (forward.sqrMagnitude < 1e-6f) return RerollRestPosition();
            forward.Normalize();
            var lateral = new Vector2(-forward.y, forward.x);
            float r = TuneCircleRadius;
            float lat = StagePerformanceConfig.AdvanceRestLateral;
            Vector2 offset =
                forward * (r * Random.Range(StagePerformanceConfig.AdvanceRestForwardMin,
                                            StagePerformanceConfig.AdvanceRestForwardMax))
                + lateral * (r * Random.Range(-lat, lat));
            RestPosition = AnchorAtOffset(ClampToTuneCircle(offset));
            return RestPosition;
        }

        /// <summary>位移回位：先重采样休息点，再 tween 过去（演出层观感抖动，不影响结算）。</summary>
        public Tween DOMoveReturnHome(float duration, Ease ease = Ease.OutQuad)
        {
            return transform.DOMove(RerollRestPosition(), duration)
                .SetEase(ease).SetLink(gameObject);
        }

        /// <summary>出击收势回位：落点沿 towardWorld 方向前移（见
        /// <see cref="RerollRestPositionToward"/>）。</summary>
        public Tween DOMoveReturnHomeToward(Vector3 towardWorld, float duration,
                                            Ease ease = Ease.OutQuad)
        {
            return transform.DOMove(RerollRestPositionToward(towardWorld), duration)
                .SetEase(ease).SetLink(gameObject);
        }

        /// <summary>受击顿挫：卡根**定向击退**（沿受击线）→ 落定后**沿线前后颤** +
        /// 立绘挤压 + 红闪。
        ///
        /// 受击线 = 「攻击方站位中心 → 本卡站位中心」（2026-07-27 定案）。
        /// 取**站位中心点**（<see cref="HomePosition"/>）而不是当前 transform：
        /// 攻击方突进后就贴在身边，用实时位置算出的方向会乱跳甚至反向。
        ///
        /// 三条通道刻意分开、时间上串行，互不代偿：
        ///   击退＝**定位点位移**（力的方向，推开与落定都钉在受击线上，微调圆截断）；
        ///   颤动＝落定后围绕落点沿同线的**纯动画**前后颤（结束回落点，不改定位点）；
        ///   挤压＝**立绘形变**（肉感）。
        /// 颤动排在击退之后而不是同时：同时跑会互相抵消，方向和力度都读不清。
        ///
        /// 绕身**默认不影响受击**（2026-07-28 人工定案）：罩由 `VfxShroudFollower`
        /// 跟着卡走，甩不出去，而"罩身回合完全没有受击反馈"是把打击感整段抹掉。
        /// 只有注册表显式置了 `shroudLocksHitMotion` 的状态（定身/结界类语义）
        /// 才禁卡根位移，此时挤压与红闪照给。
        ///
        /// fromHome＝伤害来源的站位中心；省略（环境/状态伤）则不击退，
        /// 原地沿纵深轴起颤+挤压。</summary>
        public void HitReact(bool isCrit, Vector3? fromHome = null)
        {
            transform.DOKill(true);
            CancelHitTremble();
            // 只有显式声明"锁受击位移"的罩身才禁卡根位移；其余罩身照常击退+颤动。
            if (!UnitAuraService.HasHitMotionLock(this) && !KnockBack(isCrit, fromHome))
            {
                // 没有可用的受击线（环境/状态伤、同格）：原地起颤，方向取
                // 地面纵深轴（朝观众），围绕当前位置，结束归位。
                StartHitTremble(isCrit, new Vector2(0f, -1f), transform.position);
            }
            _idleMotion.Punch(isCrit ? 1f : 0.6f, ShoveLocal(fromHome));
            FlashPortrait(isCrit ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.55f, 0.55f));
        }

        /// <summary>定向击退：把**定位点**沿受击线随机后退一段（微调圆截断），
        /// 位移两段（推开→落定）**全部钉在受击线上**。返回是否真的退了。
        ///
        /// 【为什么落定点不再随机重采样】旧版推开点在受击线上、回弹却奔一个
        /// 圆盘随机点去——第二段位移斜出受击线，观感就是"击退方向不对"。
        /// 现在落定点＝线上的随机后退距离，推开点＝同线上再过冲一点，
        /// 抖动（沿线前后颤）在落定之后接手。</summary>
        bool KnockBack(bool isCrit, Vector3? fromHome)
        {
            if (fromHome == null) return false;
            // 「攻击方站位中心 → 本卡站位中心」＝被推开的方向
            Vector2 dir = -OffsetFromHome(fromHome.Value);
            if (dir.sqrMagnitude < 1e-6f) return false; // 自伤/同格：不击退
            dir.Normalize();

            // 后退点**严格落在受击线上**：以 Home 为起点、沿 dir 走一段随机距离。
            // 不从当前位置累加——当前位置可能已被上一发推偏，累加会让卡牌
            // 一路斜着漂出受击线，也会连着挨打时越推越远。
            float dist = TuneCircleRadius * (isCrit
                ? Random.Range(StagePerformanceConfig.KnockbackCritMin,
                               StagePerformanceConfig.KnockbackCritMax)
                : Random.Range(StagePerformanceConfig.KnockbackNormalMin,
                               StagePerformanceConfig.KnockbackNormalMax));
            // 微调圆是唯一的封顶处（配置被改到 >1 倍半径时也不会越圆）
            Vector2 settleOffset = ClampToTuneCircle(dir * dist);
            Vector3 settle = AnchorAtOffset(settleOffset);
            // 推开点＝同一条线上再过冲一点（同样被圆截断），回弹落回 settle
            Vector3 shoved = AnchorAtOffset(ClampToTuneCircle(
                dir * (dist * Mathf.Max(1f, StagePerformanceConfig.KnockOvershoot))));
            RestPosition = settle; // 击退移动的就是定位点，后续回位动画同源

            var seq = DOTween.Sequence().SetLink(gameObject);
            seq.Append(transform.DOMove(shoved, isCrit
                    ? StagePerformanceConfig.KnockOutSecondsCrit
                    : StagePerformanceConfig.KnockOutSecondsNormal)
                .SetEase(Ease.OutQuad));
            seq.Append(transform.DOMove(settle, isCrit
                    ? StagePerformanceConfig.KnockBackSecondsCrit
                    : StagePerformanceConfig.KnockBackSecondsNormal)
                .SetEase(Ease.OutQuad));
            // 抖动在击退**结束后**接手：沿同一条受击线前后颤，纯动画、围绕落点
            seq.OnComplete(() => StartHitTremble(isCrit, dir, settle));
            return true;
        }

        // ---- 受击抖动（沿受击线前后颤，纯动画）----
        //
        // 2026-07-27 重做：旋转式抖动在近正面卡上怎么调都读不出来（面内自旋不改
        // 轮廓、俯仰被投影吃掉）。改为**位置颤动**：击退落定后，围绕落点沿同一条
        // 受击线小幅前后颤，衰减归零后回到落点——纯动画，不改定位点。
        // 与"位移归击退"不冲突：颤动发生在击退结束之后，两者在时间上不重叠。
        float _trembleLeft, _trembleTotal, _trembleAmp;
        Vector3 _trembleCenter, _trembleAxis;

        /// <summary>起颤。dirGround＝受击线方向（地面二维），center＝围绕的落点。</summary>
        void StartHitTremble(bool isCrit, Vector2 dirGround, Vector3 center)
        {
            if (Defeated || _petrified) return; // 尸位/石化是静止像
            _trembleTotal = isCrit ? StagePerformanceConfig.HitTrembleSecondsCrit
                                   : StagePerformanceConfig.HitTrembleSecondsNormal;
            _trembleLeft = _trembleTotal;
            _trembleCenter = center;
            // 地面二维方向 → 世界轴（近 3D 下必须经地面映射，直接用世界向量会错纵深）
            Vector3 axis = AnchorAtOffset(dirGround) - AnchorAtOffset(Vector2.zero);
            _trembleAxis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.zero;
            _trembleAmp = TuneCircleRadius * (isCrit
                ? StagePerformanceConfig.HitTrembleAmpCrit
                : StagePerformanceConfig.HitTrembleAmpNormal);
        }

        void CancelHitTremble()
        {
            _trembleLeft = 0f;
        }

        /// <summary>颤动逐帧驱动。任何 tween 接管 transform（回位/突进/倒下）即让位——
        /// 颤动是最低优先级的收尾装饰，不许跟正经位移打架。</summary>
        void TickHitTremble(float dt)
        {
            if (_trembleLeft <= 0f) return;
            if (Defeated || _petrified || DOTween.IsTweening(transform))
            {
                _trembleLeft = 0f;
                return;
            }
            _trembleLeft = Mathf.Max(0f, _trembleLeft - dt);
            float k = _trembleTotal > 0f ? _trembleLeft / _trembleTotal : 0f;
            if (k <= 0f)
            {
                transform.position = _trembleCenter; // 收干净：动画结束回到落点
                return;
            }
            float amp = _trembleAmp *
                        Mathf.Pow(k, Mathf.Max(0.2f, StagePerformanceConfig.HitTrembleDecayPower));
            float t = (_trembleTotal - _trembleLeft)
                      * StagePerformanceConfig.HitTrembleFrequency * Mathf.PI * 2f;
            transform.position = _trembleCenter + _trembleAxis * (Mathf.Sin(t) * amp);
        }

        /// <summary>受力方向换算成卡局部 xy 单位向量（供立绘挤压/侧倾）。
        /// 必须转局部：近 3D 下卡牌后倾 45°，直接用世界向量会把纵深错算成上下。</summary>
        Vector2 ShoveLocal(Vector3? fromHome)
        {
            if (fromHome == null) return Vector2.zero;
            var local = transform.InverseTransformDirection(HomePosition - fromHome.Value);
            var dir = new Vector2(local.x, local.y);
            return dir.sqrMagnitude < 1e-6f ? Vector2.zero : dir.normalized;
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
            _idleMotion.SetFrozen(frozen);

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

            // 含势能火 / 卡后金光环（AuraMount_*）
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
            _idleMotion.SetFrozen(true); // 尸位不呼吸：倒下后必须是完全静止像
            SetAresRage(false);
            MomentumFire.Extinguish();
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
            RestPosition = SampleRestAroundHome();
            transform.position = RestPosition;
            ApplyCardLean(); // 阵亡倒下改过 rotation，复活必须回到固定卡姿
            // 立绘姿态可能停在阵亡冻结帧上：重绑基准并解冻，否则下一局带着歪斜复活
            _portrait.transform.localPosition =
                new Vector3(0f, _portraitBaseY, -PortraitDepth * _layoutScale);
            _portrait.transform.localRotation = Quaternion.identity;
            FitSpriteToSlot(_portrait, _portraitW, _portraitH);
            _idleMotion.Bind(_portrait.transform, _layoutScale, _idlePhase);
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
        // 卡面尺寸由 StanceLayout 按区域反算；本类用 _frameW/_frameH + LayoutScale。

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
            go.transform.localScale = Vector3.one * (0.08f * Mathf.Max(0.35f, _layoutScale));
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
