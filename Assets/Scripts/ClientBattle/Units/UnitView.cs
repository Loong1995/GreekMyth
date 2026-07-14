using ClientBattle.Events;
using ClientBattle.Placeholder;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 武将卡牌 GameObject（带子物体）：
    //   Frame（阵营色卡框 Sprite）── Portrait（立绘占位）── NameLabel（TextMesh）
    //   ── HpBar（背景+填充）── StatusIconPanel（上方常规图标 + 中央控制图标）
    //   ── PetrifyOverlay（石化边框变灰版本）── BubbleAnchor（台词气泡锚点）
    // 立绘回退：Resources/ClientBattle/Portraits/<template_id>.png → 阵营色占位。
    // =========================================================================

    public class UnitView : MonoBehaviour
    {
        public HeroSnapshot Hero { get; private set; }
        public string TeamId { get; private set; }
        public Vector3 HomePosition { get; private set; }
        public StatusIconPanel StatusPanel { get; private set; }
        public Transform BubbleAnchor { get; private set; }

        public int CurrentTroops { get; private set; }
        public bool Defeated { get; private set; }

        SpriteRenderer _frame, _portrait, _hpFill, _petrifyOverlay;
        TextMesh _nameLabel, _hpLabel;
        Color _frameColor;
        const float HpBarWidth = 1.5f;
        float _idlePhase; // 待机呼吸相位（按卡错开，不同步摆动）

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
            CurrentTroops = hero.InitialTroops;
            _frameColor = factionColor;
            transform.position = position;

            // 卡框：真实资源 Resources/ClientBattle/CardFrames/frame.png（白底图，
            // 按阵营色染色）→ 占位为阵营色圆角色块
            var realFrame = PlaceholderFactory.TryLoadSprite("CardFrames", "frame");
            _frame = NewSprite("Frame",
                realFrame != null ? realFrame : PlaceholderFactory.MakeSolidSprite(factionColor, 96), 0);
            if (realFrame != null) _frame.color = factionColor;
            _frame.transform.localScale = new Vector3(1.7f, 2.3f, 1f);

            // 立绘（真实资源：Resources/ClientBattle/Portraits/<template_id>.png）
            var portraitSprite = PlaceholderFactory.GetSprite(
                "Portraits", hero.TemplateId, Color.Lerp(factionColor, Color.black, 0.35f), 96);
            _portrait = NewSprite("Portrait", portraitSprite, 1);
            _portrait.transform.localScale = new Vector3(1.45f, 1.7f, 1f);
            _portrait.transform.localPosition = new Vector3(0f, 0.18f, 0f);

            // 名字
            _nameLabel = NewText("NameLabel", hero.HeroId, 42, Color.white,
                new Vector3(0f, -0.78f, 0f), 2);

            // 血条
            NewSprite("HpBack", PlaceholderFactory.MakeSolidSprite(new Color(0.12f, 0.12f, 0.12f), 8), 2)
                .transform.SetLocalPositionAndScale(new Vector3(0f, -1.0f, 0f), new Vector3(HpBarWidth, 0.14f, 1f));
            _hpFill = NewSprite("HpFill", PlaceholderFactory.MakeSolidSprite(new Color(0.35f, 0.9f, 0.4f), 8), 3);
            _hpFill.transform.SetLocalPositionAndScale(new Vector3(0f, -1.0f, 0f), new Vector3(HpBarWidth, 0.14f, 1f));
            // 中性浅灰数字：深/浅背景下都可读（背景无色时呈黑底）
            _hpLabel = NewText("HpLabel", CurrentTroops.ToString(), 30, new Color(0.8f, 0.8f, 0.82f),
                new Vector3(0f, -1.18f, 0f), 4);

            // 石化覆盖层（默认隐藏；石化时显示灰色边框版本）
            _petrifyOverlay = NewSprite("PetrifyOverlay",
                PlaceholderFactory.GetSprite("CardFrames", "petrify", new Color(0.55f, 0.55f, 0.5f, 0.85f), 96), 5);
            _petrifyOverlay.transform.localScale = new Vector3(1.7f, 2.3f, 1f);
            _petrifyOverlay.gameObject.SetActive(false);

            // 状态图标面板
            var panelGo = new GameObject("StatusIconPanel");
            panelGo.transform.SetParent(transform, false);
            StatusPanel = panelGo.AddComponent<StatusIconPanel>();

            // 台词气泡锚点（卡牌右上）
            var anchor = new GameObject("BubbleAnchor");
            anchor.transform.SetParent(transform, false);
            anchor.transform.localPosition = new Vector3(0.7f, 1.35f, 0f);
            BubbleAnchor = anchor.transform;

            // 待机呼吸相位按位置错开：全场卡牌不同步摆动
            _idlePhase = (position.x * 0.73f + position.y * 1.31f) * 2.4f;
        }

        void Update()
        {
            // 待机呼吸：立绘轻微上下浮动。画面任何时刻都有活物，杜绝"静止帧=
            // 卡死"的观感（实测 60fps 满帧仍被感知为卡，病根是全场静止）。
            if (Defeated || _portrait == null) return;
            float bob = Mathf.Sin(Time.time * 2.1f + _idlePhase) * 0.035f;
            var p = _portrait.transform.localPosition;
            _portrait.transform.localPosition = new Vector3(p.x, 0.18f + bob, p.z);
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

        /// <summary>受击顿挫：短促位移抖动 + 红闪。</summary>
        public void HitReact(bool isCrit)
        {
            transform.DOKill(true);
            transform.DOShakePosition(isCrit ? 0.3f : 0.18f, isCrit ? 0.22f : 0.12f, 20)
                .OnComplete(() => transform.position = Defeated ? transform.position : HomePosition);
            FlashPortrait(isCrit ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.55f, 0.55f));
        }

        void FlashPortrait(Color flash)
        {
            var original = Color.white;
            _portrait.color = flash;
            DOTween.To(() => _portrait.color, c => _portrait.color = c, original, 0.25f);
        }

        Tween _petrifyTween;

        /// <summary>石化开关：卡牌边框换石化版本；解除时渐变回来（配石头脱落音效由表演层放）。
        /// 施加/解除的渐变互斥：新调用先杀旧 tween，防止旧的 OnComplete 把新状态关掉。</summary>
        public void SetPetrified(bool on)
        {
            _petrifyTween?.Kill();
            _petrifyTween = null;
            if (on)
            {
                _petrifyOverlay.gameObject.SetActive(true);
                _petrifyOverlay.color = new Color(1f, 1f, 1f, 0f);
                _petrifyTween = DOTween.To(
                    () => _petrifyOverlay.color, c => _petrifyOverlay.color = c, Color.white, 0.3f);
            }
            else if (_petrifyOverlay.gameObject.activeSelf)
            {
                _petrifyTween = DOTween.To(() => _petrifyOverlay.color, c => _petrifyOverlay.color = c,
                        new Color(1f, 1f, 1f, 0f), 0.5f)
                    .OnComplete(() => _petrifyOverlay.gameObject.SetActive(false));
            }
        }

        /// <summary>压暗（单挑聚焦等场合非参战单位调暗）。</summary>
        public void SetDimmed(bool dimmed)
        {
            var tint = dimmed ? new Color(0.4f, 0.4f, 0.45f) : Color.white;
            _portrait.color = tint;
            _frame.color = dimmed ? Color.Lerp(_frameColor, Color.black, 0.55f) : _frameColor;
        }

        /// <summary>阵亡：变灰倒下（保留尸位，主将阵亡由横幅另行强调）。</summary>
        public void PlayDefeated()
        {
            Defeated = true;
            transform.DOKill();
            _portrait.color = new Color(0.35f, 0.35f, 0.35f);
            _frame.color = new Color(0.3f, 0.3f, 0.3f);
            transform.DORotate(new Vector3(0f, 0f, TeamId == "A" ? -80f : 80f), 0.45f)
                .SetEase(Ease.InQuad);
            transform.DOMove(HomePosition + new Vector3(0f, -0.25f, 0f), 0.45f);
        }

        /// <summary>整局重置（下一局开始时复活重摆）。</summary>
        public void ResetForNewGame(int initialTroops)
        {
            Defeated = false;
            transform.DOKill();
            transform.position = HomePosition;
            transform.rotation = Quaternion.identity;
            _portrait.color = Color.white;
            _frame.color = _frameColor;
            SetPetrified(false);
            SetTroops(initialTroops);
            StatusPanel.Clear();
        }

        // ---------------------------------------------------------- 工具

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
