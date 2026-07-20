using System.Collections.Generic;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 战场棋盘：按战报 teams 快照生成双方 UnitView（A 队下、B 队上，按站位横排），
    // 提供 unitId → UnitView / Transform 查询、棋盘中心点、整盘滤镜特效挂点。
    // 阵营配色与 docs/client 视觉规范一致（olympus 金 / heroes 红 / sea 蓝 / underworld 紫）。
    // =========================================================================

    public class BattleBoardView : MonoBehaviour
    {
        static readonly Dictionary<string, Color> FactionColors = new()
        {
            ["olympus"] = new Color(0.85f, 0.72f, 0.25f),
            ["heroes"] = new Color(0.78f, 0.28f, 0.22f),
            ["sea"] = new Color(0.22f, 0.55f, 0.82f),
            ["underworld"] = new Color(0.55f, 0.30f, 0.72f),
        };

        // template_id → faction（与 battle/roster.py 同步；未登记按 heroes 配色。
        // A4：gods→olympus、men→heroes；奥德修斯→sea、赫尔墨斯→underworld）
        static readonly Dictionary<string, string> FactionOf = new()
        {
            ["zeus"] = "olympus", ["athena"] = "olympus", ["ares"] = "olympus",
            ["apollo"] = "olympus", ["asclepius"] = "olympus", ["artemis"] = "olympus",
            ["nike"] = "olympus",
            ["achilles"] = "heroes", ["heracles"] = "heroes", ["perseus"] = "heroes",
            ["atalanta"] = "heroes", ["paris"] = "heroes", ["ajax"] = "heroes",
            ["hector"] = "heroes", ["jason"] = "heroes", ["castor"] = "heroes",
            ["poseidon"] = "sea", ["amphitrite"] = "sea", ["triton"] = "sea",
            ["siren"] = "sea", ["scylla"] = "sea", ["odysseus"] = "sea",
            ["hades"] = "underworld", ["medusa"] = "underworld", ["persephone"] = "underworld",
            ["charon"] = "underworld", ["thanatos"] = "underworld", ["cerberus"] = "underworld",
            ["hermes"] = "underworld",
        };

        readonly Dictionary<string, UnitView> _units = new();
        BattleReport _report;

        /// <summary>棋盘中心（群攻战法施法者移动的落点）。</summary>
        public Vector3 Center => transform.position;

        /// <summary>整盘滤镜挂点（海洋滤镜/血色呼吸等全屏级特效挂这里）。</summary>
        public Transform BoardFxRoot { get; private set; }

        public IEnumerable<UnitView> AllUnits => _units.Values;

        public void Build(BattleReport report)
        {
            _report = report;
            Clear();

            var fxRoot = new GameObject("BoardFxRoot");
            fxRoot.transform.SetParent(transform, false);
            BoardFxRoot = fxRoot.transform;

            BuildBackground();

            for (int teamIdx = 0; teamIdx < report.Teams.Count; teamIdx++)
            {
                var team = report.Teams[teamIdx];
                // 上下布局：A 队在下方、B 队在上方，队内按站位从左到右横排
                float y = teamIdx == 0 ? -2.6f : 2.6f;
                foreach (var hero in team.Heroes)
                {
                    float x = (hero.Position - (team.Heroes.Count - 1) * 0.5f) * 3.0f;
                    string faction = FactionOf.TryGetValue(hero.TemplateId, out var f) ? f : "heroes";
                    var unit = UnitView.Create(hero, team.TeamId,
                        FactionColors[faction], transform.position + new Vector3(x, y, 0f));
                    unit.transform.SetParent(transform, true);
                    _units[hero.HeroId] = unit;
                }
            }
        }

        /// <summary>棋盘背景：无色（相机透明底，独立运行时显示为黑）。
        /// 上传 Resources/ClientBattle/UI/board_background.png 后自动改为真图
        /// cover 铺满（BackgroundFitter 每帧跟随，分辨率热切换安全）。</summary>
        void BuildBackground()
        {
            var cam = Camera.main;
            var real = Placeholder.PlaceholderFactory.TryLoadSprite("UI", "board_background");
            if (real == null)
            {
                if (cam != null) cam.backgroundColor = Color.black; // 无色=中性纯黑（alpha 1，截图/各端一致）
                return; // 无底图 → 不放任何底板
            }
            var go = new GameObject("BoardBackground");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 5f); // 压到所有卡牌/特效之后
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = real;
            renderer.sortingOrder = -100;
            go.AddComponent<BackgroundFitter>();
        }

        public UnitView Unit(string heroId) =>
            heroId != null && _units.TryGetValue(heroId, out var unit) ? unit : null;

        public Transform UnitTransform(string heroId) => Unit(heroId)?.transform;

        public void ResetForNewGame()
        {
            foreach (var unit in _units.Values)
                unit.ResetForNewGame(unit.Hero.InitialTroops);
            foreach (Transform fx in BoardFxRoot) Destroy(fx.gameObject);
        }

        public void Clear()
        {
            foreach (var unit in _units.Values)
                if (unit != null) Destroy(unit.gameObject);
            _units.Clear();
            if (BoardFxRoot != null) Destroy(BoardFxRoot.gameObject);
            var bg = transform.Find("BoardBackground");
            if (bg != null) Destroy(bg.gameObject);
        }
    }

    /// <summary>背景铺满器：按当前相机正交视野缩放 Sprite（等比放大裁切式覆盖，
    /// 不拉伸变形），分辨率变化每帧跟随——机型兼容依赖此组件而非一次性取值。</summary>
    public class BackgroundFitter : MonoBehaviour
    {
        SpriteRenderer _renderer;

        void Awake() => _renderer = GetComponent<SpriteRenderer>();

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic || _renderer.sprite == null) return;
            var spriteSize = _renderer.sprite.bounds.size;
            float viewH = cam.orthographicSize * 2f + 1f;
            float viewW = viewH * cam.aspect + 1f;
            // cover 模式：等比缩放到两边都盖住（真图不变形，两侧/上下超出裁掉）
            float scale = Mathf.Max(viewW / spriteSize.x, viewH / spriteSize.y);
            transform.localScale = new Vector3(scale, scale, 1f);
            transform.position = new Vector3(
                cam.transform.position.x, cam.transform.position.y, transform.position.z);
        }
    }
}
