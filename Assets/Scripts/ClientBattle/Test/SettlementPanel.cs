using System.Collections.Generic;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.Test
{
    // =========================================================================
    // 战后结算面板（三谋式：左右分队；多局时顶部 Tab 切换分局/系列合计）。
    // 原嵌在 PerformanceRunner OnGUI 内，2026-07-22 拆出；仍为 OnGUI 渲染。
    // 数据由 BattleSkillStatsAggregator 生成，本面板只负责绘制与 Tab 状态。
    // =========================================================================

    public class SettlementPanel : MonoBehaviour
    {
        public static SettlementPanel Instance { get; private set; }

        /// <summary>面板是否正在显示（BannerService 依此让位）。</summary>
        public static bool Visible => Instance != null && Instance._show;

        bool _show;
        BattleSettlementSnapshot _settlement;
        Vector2 _scroll;
        int _tab; // Games 列表下标

        GUIStyle _titleStyle, _heroStyle, _skillStyle, _btnStyle;

        public static SettlementPanel Ensure()
        {
            if (Instance == null)
                Instance = new GameObject("SettlementPanel").AddComponent<SettlementPanel>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>显示结算快照（重复调用刷新数据、Tab 归零钳制）。</summary>
        public void Show(BattleSettlementSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Games.Count == 0)
            {
                Debug.LogWarning("[ClientBattle] 结算表为空（战报无对局）");
                return;
            }
            _settlement = snapshot;
            _tab = Mathf.Clamp(_tab, 0, snapshot.Games.Count - 1);
            _show = true;
            VFX.BannerService.Suppressed = true; // 横幅让位（依赖方向：UI→基础设施）
        }

        public void Hide()
        {
            _show = false;
            VFX.BannerService.Suppressed = false;
        }

        void OnGUI()
        {
            if (!_show || _settlement == null) return;
            float k = Mathf.Max(1f, Screen.height / 800f);
            DrawSettlement(k);
        }

        void DrawSettlement(float k)
        {
            if (_settlement.Games.Count == 0) return;
            _tab = Mathf.Clamp(_tab, 0, _settlement.Games.Count - 1);
            var snap = _settlement.Games[_tab];

            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
            };
            _btnStyle ??= new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = Mathf.RoundToInt(14 * k);

            // 系列胜负标题
            _titleStyle.fontSize = Mathf.RoundToInt(28 * k);
            _titleStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
            string series = string.IsNullOrEmpty(_settlement.SeriesWinnerTeamId) ? "系列平局"
                : $"系列胜者 {_settlement.SeriesWinnerTeamId} 队";
            GUI.Label(new Rect(0, 6 * k, Screen.width, 32 * k), series, _titleStyle);

            // 分局 Tab
            float tabY = 40 * k;
            float tabH = 28 * k;
            float tabW = 100 * k;
            float tabsWidth = _settlement.Games.Count * (tabW + 6 * k);
            float tabX0 = (Screen.width - tabsWidth) * 0.5f;
            for (int i = 0; i < _settlement.Games.Count; i++)
            {
                var g = _settlement.Games[i];
                var label = g.GameNo == 0 ? "系列合计" : $"第 {g.GameNo} 局";
                if (GUI.Toggle(new Rect(tabX0 + i * (tabW + 6 * k), tabY, tabW, tabH),
                        _tab == i, label, _btnStyle) && _tab != i)
                {
                    _tab = i;
                    _scroll = Vector2.zero;
                }
            }

            // 本 Tab 胜负副标题
            _titleStyle.fontSize = Mathf.RoundToInt(20 * k);
            _titleStyle.normal.textColor = Color.white;
            string winner = string.IsNullOrEmpty(snap.WinnerTeamId) ? $"{snap.Title} · 平局"
                : $"{snap.Title} · {snap.WinnerTeamId} 队胜";
            GUI.Label(new Rect(0, 72 * k, Screen.width, 28 * k), winner, _titleStyle);

            float mid = Screen.width * 0.5f;
            float colW = Screen.width * 0.42f;
            float leftX = mid - colW - 8 * k;
            float rightX = mid + 8 * k;
            float top = 104 * k;
            float height = Screen.height - top - 56 * k;

            _scroll = GUI.BeginScrollView(
                new Rect(0, top, Screen.width, height),
                _scroll,
                new Rect(0, 0, Screen.width - 20 * k, Mathf.Max(height, EstimateHeight(snap, k))));

            DrawTeamColumn(snap.TeamA, leftX, 0, colW, k, isWinner: snap.WinnerTeamId == snap.TeamAId);
            DrawTeamColumn(snap.TeamB, rightX, 0, colW, k, isWinner: snap.WinnerTeamId == snap.TeamBId);

            GUI.EndScrollView();

            _btnStyle.fontSize = Mathf.RoundToInt(16 * k);
            float bw = 140 * k, bh = 36 * k;
            if (GUI.Button(new Rect(mid - bw * 0.5f, Screen.height - 48 * k, bw, bh),
                    "关闭结算", _btnStyle))
                Hide();
        }

        float EstimateHeight(GameSettlementSnapshot snap, float k)
        {
            int rows = 0;
            foreach (var h in snap.TeamA) rows += 2 + h.Skills.Count;
            foreach (var h in snap.TeamB) rows += 2 + h.Skills.Count;
            return rows * 22 * k + 80 * k;
        }

        void DrawTeamColumn(List<HeroSkillStats> heroes, float x, float y, float w, float k, bool isWinner)
        {
            _heroStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _skillStyle ??= new GUIStyle(GUI.skin.label);
            _heroStyle.fontSize = Mathf.RoundToInt(16 * k);
            _skillStyle.fontSize = Mathf.RoundToInt(13 * k);

            float cy = y;
            foreach (var hero in heroes)
            {
                float ratio = hero.MaxTroops > 0
                    ? Mathf.Clamp01((float)hero.FinalTroops / hero.MaxTroops) : 0f;
                // 兵力条
                var barBg = new Rect(x, cy, w, 18 * k);
                GUI.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
                GUI.DrawTexture(barBg, Texture2D.whiteTexture);
                GUI.color = isWinner ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.45f, 0.45f, 0.5f);
                GUI.DrawTexture(new Rect(x, cy, w * ratio, 18 * k), Texture2D.whiteTexture);
                GUI.color = Color.white;
                _heroStyle.normal.textColor = Color.white;
                GUI.Label(barBg, $" {hero.HeroId}  {hero.FinalTroops}/{hero.MaxTroops}",
                    _heroStyle);
                cy += 22 * k;

                foreach (var skill in hero.Skills)
                {
                    if (skill.Triggers <= 0 && skill.Damage <= 0 && skill.Heal <= 0) continue;
                    string name = skill.DisplayName;
                    string line = $"  {name}  ×{skill.Triggers}";
                    if (skill.Damage > 0) line += $"  ⚔{skill.Damage}";
                    if (skill.Heal > 0) line += $"  +{skill.Heal}";
                    _skillStyle.normal.textColor = new Color(0.9f, 0.9f, 0.85f);
                    GUI.Label(new Rect(x, cy, w, 18 * k), line, _skillStyle);
                    cy += 18 * k;
                }
                cy += 10 * k;
            }
        }
    }
}
