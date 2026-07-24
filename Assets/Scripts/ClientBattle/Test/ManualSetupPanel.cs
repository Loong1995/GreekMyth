using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Test
{
    // =========================================================================
    // 手动配阵测试页：每队 6 站位（1~3 前排、4~6 后排），B 上 A 下镜面布局，
    // 中间「对战 1 次 / 对战 100 次」。每队最多上阵 3 人。空位点击弹武将池
    // （也可拖已上阵武将换位），战法空格显示 +。对战走战报播放 + 结算。
    // =========================================================================

    public class ManualSetupPanel : MonoBehaviour
    {
        public int Seed = 7;
        public int StatsBattles = 100;
        [Range(0.25f, 4f)] public float Speed = 1f;
        [Tooltip("战斗服务地址（battle_server.py）；不可达时编辑器/桌面自动回退本机 python 子进程")]
        public string ServerUrl = "http://127.0.0.1:8017";

        string _serverUrlEdit;
        string _transport = "";

        ManualCatalog _catalog;
        // 0~5 = A 站位 1~6；6~11 = B 站位 1~6
        readonly ManualSlot[] _slots = new ManualSlot[12];

        int _heroPickerSlot = -1;
        int _skillPickerSlot = -1, _skillPickerCell = -1;
        string _skillPickerChoice;
        int _heroDetailSlot = -1;
        int _skillDetailSlot = -1, _skillDetailCell = -2;

        ManualBattleBridge _job;
        string _jobKind;
        string _error;
        bool _playing;
        string _lastReportJson;
        ManualStats _stats;
        Vector2 _statsScroll, _pickerScroll;

        int _dragFrom = -1;
        Vector2 _dragMouse;
        Vector2 _dragStart;
        bool _dragMoved;
        const float DragThresholdPx = 8f;
        const int MaxHeroesPerTeam = 3;

        PerformanceRunner _runner;
        GUIStyle _label, _bold, _btn, _cell;

        static int StanceOf(int slotIdx) => slotIdx % 6 + 1;
        static bool IsTeamA(int slotIdx) => slotIdx < 6;
        static string SlotTitle(int slotIdx)
        {
            int st = StanceOf(slotIdx);
            string row = st <= 3 ? "前" : "后";
            return $"{(IsTeamA(slotIdx) ? "A" : "B")}{st}·{row}";
        }

        /// <summary>播放中、或结算表还开着：都压住配阵页，避免叠层。</summary>
        bool BattleChromeVisible =>
            _playing || SettlementPanel.Visible;

        void Start()
        {
            QualitySettings.vSyncCount = 1;
            Application.runInBackground = true;
#if !UNITY_EDITOR
            if (Screen.fullScreen) Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
#endif
            ManualBattleBridge.ServerUrl = ServerUrl;
            _serverUrlEdit = ServerUrl;
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new ManualSlot();
            StartJob(ManualBattleBridge.FetchCatalog(), "catalog");
        }

        void StartJob(ManualBattleBridge job, string kind)
        {
            _job = job;
            _jobKind = kind;
            _error = null;
        }

        void Update()
        {
            if (_runner != null) _runner.Speed = Speed;

            if (_job == null || !_job.Done) return;
            var job = _job;
            _job = null;
            if (job.Error != null)
            {
                _error = job.Error;
                Debug.LogError($"[ManualSetup] {_jobKind} 失败: {job.Error}");
                return;
            }
            _transport = job.Transport;
            switch (_jobKind)
            {
                case "catalog":
                    _catalog = ManualCatalog.Parse(job.ResultJson);
                    break;
                case "once":
                    _playing = true;
                    _lastReportJson = job.ResultJson;
                    _runner = PerformanceRunner.Ensure();
                    _runner.Speed = Speed;
                    _runner.PlayBattleReport(_lastReportJson);
                    break;
                case "stats":
                    _stats = ManualStats.Parse(job.ResultJson);
                    _statsScroll = Vector2.zero;
                    break;
            }
        }

        /// <summary>回配阵：停播 + 拆战场可视 + 关结算（禁止 SkipToEnd 再弹结算）。</summary>
        void ReturnToSetup()
        {
            _playing = false;
            CancelDrag();
            if (SettlementPanel.Instance != null) SettlementPanel.Instance.Hide();
            _runner?.TeardownWorld();
        }

        void ReplayLastReport()
        {
            if (string.IsNullOrEmpty(_lastReportJson)) return;
            if (SettlementPanel.Instance != null) SettlementPanel.Instance.Hide();
            _runner = PerformanceRunner.Ensure();
            _runner.Speed = Speed;
            // PlayBattleReport 内会 StopPlayback；此处显式先停，避免 UI 连点竞态
            _runner.StopPlayback();
            _runner.PlayBattleReport(_lastReportJson);
        }

        void CancelDrag()
        {
            _dragFrom = -1;
            _dragMoved = false;
            if (GUIUtility.hotControl != 0) GUIUtility.hotControl = 0;
        }

        // ------------------------------------------------------------ 校验/操作

        IEnumerable<int> TeamIndices(int slotIdx)
        {
            int start = IsTeamA(slotIdx) ? 0 : 6;
            for (int i = start; i < start + 6; i++) yield return i;
        }

        bool TemplateUsedInTeam(int slotIdx, string templateId)
        {
            foreach (int i in TeamIndices(slotIdx))
                if (i != slotIdx && _slots[i].TemplateId == templateId) return true;
            return false;
        }

        bool SkillUsedOnHero(ManualSlot slot, string skillId)
        {
            var hero = _catalog.HeroOf(slot.TemplateId);
            if (hero != null && hero.InnateSkill == skillId) return true;
            foreach (var s in slot.ExtraSkills) if (s == skillId) return true;
            return false;
        }

        int FilledCount(int teamStart)
        {
            int n = 0;
            for (int i = teamStart; i < teamStart + 6; i++)
                if (!_slots[i].IsEmpty) n++;
            return n;
        }

        bool TeamReady(int teamStart) => FilledCount(teamStart) >= 1;

        bool TeamsValid(out string err)
        {
            err = null;
            int a = FilledCount(0), b = FilledCount(6);
            if (a < 1 || b < 1) { err = "两队至少各上阵 1 人"; return false; }
            if (a > MaxHeroesPerTeam || b > MaxHeroesPerTeam)
            {
                err = $"每队最多 {MaxHeroesPerTeam} 人（站位 1~6 任选）";
                return false;
            }
            return true;
        }

        void MoveOrSwap(int from, int to)
        {
            if (from == to || _slots[from].IsEmpty) return;
            // 跨队拖入空位时目标队不得超 3 人
            if (_slots[to].IsEmpty)
            {
                int toStart = IsTeamA(to) ? 0 : 6;
                int fromStart = IsTeamA(from) ? 0 : 6;
                if (toStart != fromStart && FilledCount(toStart) >= MaxHeroesPerTeam)
                    return;
            }
            var a = _slots[from];
            var b = _slots[to];
            _slots[to] = a;
            _slots[from] = b;
            if ((!a.IsEmpty && TemplateUsedInTeam(to, a.TemplateId)) ||
                (!b.IsEmpty && TemplateUsedInTeam(from, b.TemplateId)))
            {
                _slots[from] = a;
                _slots[to] = b;
            }
        }

        void LaunchOnce()
        {
            if (!TeamsValid(out var err)) { _error = err; return; }
            string cfg = ManualBattleBridge.BuildConfigJson(TeamSlots(0), TeamSlots(6));
            StartJob(ManualBattleBridge.RunOnce(cfg, Seed), "once");
        }

        void LaunchStats()
        {
            if (!TeamsValid(out var err)) { _error = err; return; }
            string cfg = ManualBattleBridge.BuildConfigJson(TeamSlots(0), TeamSlots(6));
            StartJob(ManualBattleBridge.RunStats(cfg, StatsBattles, Seed), "stats");
        }

        ManualSlot[] TeamSlots(int start)
        {
            var arr = new ManualSlot[6];
            for (int i = 0; i < 6; i++) arr[i] = _slots[start + i];
            return arr;
        }

        // ------------------------------------------------------------ OnGUI

        void OnGUI()
        {
            float k = Mathf.Max(1f, Screen.height / 800f);
            EnsureStyles(k);

            // 播放中或结算开着：只留「返回配阵」，绝不画配阵页
            if (BattleChromeVisible)
            {
                DrawPlayingOverlay(k);
                return;
            }

            HandleDragEvents(); // 全局 MouseUp/Drag，避免 HotControl 战法按钮伪影

            // 不透明底：盖住残留世界物体（Teardown 后仍可能有相机清色以外的东西）
            Color prev = GUI.color;
            GUI.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
            _bold.fontSize = Mathf.RoundToInt(24 * k);
            GUI.Label(new Rect(0, 8 * k, Screen.width, 30 * k), "手动配阵对战", _bold);

            if (_catalog == null)
            {
                _label.fontSize = Mathf.RoundToInt(18 * k);
                GUI.Label(new Rect(0, Screen.height * 0.45f, Screen.width, 30 * k),
                    _error == null ? "正在加载武将目录（python）…" : $"目录加载失败：{_error}", _label);
                if (_error != null && GUI.Button(
                        new Rect(Screen.width * 0.5f - 70 * k, Screen.height * 0.55f, 140 * k, 34 * k),
                        "重试", _btn))
                    StartJob(ManualBattleBridge.FetchCatalog(), "catalog");
                return;
            }

            DrawSlotsRow(k);
            DrawCenterControls(k);
            DrawFooter(k);
            DrawDragGhost(k);

            // 模态层（互斥）
            if (_stats != null) DrawStatsModal(k);
            else if (_heroPickerSlot >= 0) DrawHeroPicker(k);
            else if (_skillPickerSlot >= 0) DrawSkillPicker(k);
            else if (_heroDetailSlot >= 0) DrawHeroDetail(k);
            else if (_skillDetailSlot >= 0) DrawSkillDetail(k);

            if (_job != null) DrawBusyOverlay(k);
        }

        /// <summary>拖拽状态机：只拖武将卡（不拖战法按钮），松手换位或取消。</summary>
        void HandleDragEvents()
        {
            var e = Event.current;
            if (_dragFrom < 0) return;
            if (e.type == EventType.MouseDrag)
            {
                _dragMouse = e.mousePosition;
                if ((_dragMouse - _dragStart).sqrMagnitude > DragThresholdPx * DragThresholdPx)
                    _dragMoved = true;
                // 拖动中掐掉 IMGUI 按钮 HotControl，否则会留下「自带战法」按钮残影
                if (GUIUtility.hotControl != 0) GUIUtility.hotControl = 0;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                int drop = HitSlotIndex(e.mousePosition);
                if (_dragMoved && drop >= 0 && drop != _dragFrom)
                    MoveOrSwap(_dragFrom, drop);
                else if (!_dragMoved && drop == _dragFrom)
                    _heroDetailSlot = _dragFrom; // 未拖动 = 点击开武将详情
                CancelDrag();
                e.Use();
            }
        }

        int HitSlotIndex(Vector2 mouse)
        {
            for (int i = 0; i < 12; i++)
            {
                if (SlotRect(i, Mathf.Max(1f, Screen.height / 800f)).Contains(mouse))
                    return i;
            }
            return -1;
        }

        /// <summary>站位格屏幕矩形：B 后/前在上半，A 前/后在下半，中间留对战按钮带。</summary>
        Rect SlotRect(int idx, float k)
        {
            float slotW = Mathf.Min(120 * k, Screen.width / 8f);
            float slotH = 168 * k;
            float gap = 8 * k;
            float rowW = slotW * 3 + gap * 2;
            float x0 = (Screen.width - rowW) * 0.5f;
            int stance = StanceOf(idx); // 1~6
            int col = (stance - 1) % 3;
            bool back = stance >= 4;
            bool teamA = IsTeamA(idx);
            // 纵向：B后 → B前 → (中缝) → A前 → A后
            float y;
            if (!teamA && back) y = 48 * k;           // B 后排
            else if (!teamA && !back) y = 48 * k + slotH + gap; // B 前排
            else if (teamA && !back) y = 48 * k + (slotH + gap) * 2 + 70 * k; // A 前 + 中缝按钮带
            else y = 48 * k + (slotH + gap) * 3 + 70 * k; // A 后排
            float x = x0 + col * (slotW + gap);
            return new Rect(x, y, slotW, slotH);
        }

        void DrawSlotsRow(float k)
        {
            for (int i = 0; i < 12; i++)
                DrawSlot(i, SlotRect(i, k), k);
        }

        void DrawSlot(int idx, Rect rect, float k)
        {
            var slot = _slots[idx];

            GUI.color = IsTeamA(idx) ? new Color(0.35f, 0.55f, 0.95f, 0.25f)
                                     : new Color(0.95f, 0.4f, 0.35f, 0.25f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            _bold.fontSize = Mathf.RoundToInt(12 * k);
            GUI.Label(new Rect(rect.x, rect.y + 2 * k, rect.width, 16 * k),
                SlotTitle(idx), _bold);

            if (slot.IsEmpty)
            {
                GUI.enabled = _dragFrom < 0;
                if (GUI.Button(new Rect(rect.x + 4 * k, rect.y + 20 * k,
                        rect.width - 8 * k, rect.height - 26 * k), "＋", _btn))
                    _heroPickerSlot = idx;
                GUI.enabled = true;
                return;
            }

            var hero = _catalog.HeroOf(slot.TemplateId);
            float y = rect.y + 18 * k;

            var cardRect = new Rect(rect.x + 4 * k, y, rect.width - 8 * k, 64 * k);
            string card = $"{hero.Name}\n武{hero.Force} 智{hero.Intelligence}\n" +
                          $"统{hero.Command} 速{hero.Speed}";
            GUI.Box(cardRect, card, _cell);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && cardRect.Contains(e.mousePosition)
                && _dragFrom < 0 && _heroPickerSlot < 0 && _skillPickerSlot < 0
                && _stats == null)
            {
                _dragFrom = idx;
                _dragStart = e.mousePosition;
                _dragMouse = e.mousePosition;
                _dragMoved = false;
                e.Use();
            }
            if (_dragFrom == idx && _dragMoved)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.Box(cardRect, card, _cell);
                GUI.color = Color.white;
            }
            y = cardRect.yMax + 2 * k;
            bool skillsEnabled = _dragFrom < 0;
            GUI.enabled = skillsEnabled;
            y = DrawSkillCell(idx, -1, hero.InnateSkill, rect, y, k, innate: true);
            for (int c = 0; c < slot.ExtraSkills.Length; c++)
                y = DrawSkillCell(idx, c, slot.ExtraSkills[c], rect, y, k, innate: false);
            GUI.enabled = true;
        }

        void DrawDragGhost(float k)
        {
            if (_dragFrom < 0 || !_dragMoved || _slots[_dragFrom].IsEmpty) return;
            var hero = _catalog.HeroOf(_slots[_dragFrom].TemplateId);
            if (hero == null) return;
            float w = 120 * k, h = 48 * k;
            var r = new Rect(_dragMouse.x - w * 0.5f, _dragMouse.y - h * 0.5f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.Box(r, $"{hero.Name}\n{_slots[_dragFrom].TemplateId}", _cell);
            GUI.color = prev;
        }

        void EnsureStyles(float k)
        {
            _label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            _bold ??= new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _btn ??= new GUIStyle(GUI.skin.button);
            _cell ??= new GUIStyle(GUI.skin.box)
            { alignment = TextAnchor.UpperCenter, wordWrap = true };
            _btn.fontSize = Mathf.RoundToInt(14 * k);
            _cell.fontSize = Mathf.RoundToInt(11 * k);
        }

        float DrawSkillCell(int slotIdx, int cell, string skillId, Rect rect, float y, float k, bool innate)
        {
            var r = new Rect(rect.x + 4 * k, y, rect.width - 8 * k, 22 * k);
            if (string.IsNullOrEmpty(skillId))
            {
                if (GUI.Button(r, "＋", _btn))
                {
                    _skillPickerSlot = slotIdx;
                    _skillPickerCell = cell;
                    _skillPickerChoice = null;
                    _pickerScroll = Vector2.zero;
                }
            }
            else
            {
                var sk = _catalog.SkillOf(skillId);
                string label = innate ? $"◆ {sk?.Name ?? skillId}" : sk?.Name ?? skillId;
                if (GUI.Button(r, label, _btn))
                {
                    _skillDetailSlot = slotIdx;
                    _skillDetailCell = innate ? -1 : cell;
                }
            }
            return y + 24 * k;
        }

        // ------------------------------------------------------------ 中间与页脚

        void DrawCenterControls(float k)
        {
            float w = 130 * k, h = 36 * k;
            float x = Screen.width * 0.5f - w * 0.5f;
            // 落在 B 前排与 A 前排之间的中缝
            float slotH = 168 * k;
            float gap = 8 * k;
            float y = 48 * k + (slotH + gap) * 2 + 12 * k;
            bool ready = TeamReady(0) && TeamReady(6) && _job == null;

            GUI.enabled = ready;
            if (GUI.Button(new Rect(x, y, w, h), "⚔ 对战 1 次", _btn)) LaunchOnce();
            if (GUI.Button(new Rect(x, y + h + 12 * k, w, h), $"⚔ 对战 {StatsBattles} 次", _btn))
                LaunchStats();
            GUI.enabled = true;

            _label.fontSize = Mathf.RoundToInt(13 * k);
            GUI.Label(new Rect(x - 30 * k, y + 2 * (h + 12 * k), w + 60 * k, 20 * k),
                $"种子 {Seed}", _label);
            if (GUI.Button(new Rect(x + 10 * k, y + 2 * (h + 12 * k) + 22 * k, w - 20 * k, 26 * k),
                    "换种子", _btn))
                Seed = Random.Range(1, 100000);
        }

        void DrawFooter(float k)
        {
            _label.fontSize = Mathf.RoundToInt(13 * k);
            string mode = _transport == "http" ? "服务器"
                : _transport == "process" ? "本机 python（回退）" : "未连接";
            GUI.Label(new Rect(0, Screen.height - 30 * k, Screen.width, 22 * k),
                _error != null ? $"出错：{_error}"
                : $"站位 1~3 前排 / 4~6 后排 · 每队最多 3 人 · 拖拽换位 · 结算通道：{mode}",
                _label);

            // 左下：战斗服务地址编辑（连不同机器时改这里）
            float w = 260 * k, h = 24 * k;
            GUI.Label(new Rect(12 * k, Screen.height - 88 * k, w, 20 * k), "战斗服务地址：",
                GUI.skin.label);
            _serverUrlEdit = GUI.TextField(
                new Rect(12 * k, Screen.height - 64 * k, w, h), _serverUrlEdit ?? "");
            if (GUI.Button(new Rect(12 * k + w + 8 * k, Screen.height - 64 * k, 88 * k, h),
                    "连接", _btn))
            {
                ServerUrl = _serverUrlEdit.Trim();
                ManualBattleBridge.ServerUrl = ServerUrl;
                StartJob(ManualBattleBridge.FetchCatalog(), "catalog");
            }
        }

        void DrawBusyOverlay(float k)
        {
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            _bold.fontSize = Mathf.RoundToInt(22 * k);
            string text = _jobKind == "stats" ? $"正在结算 {StatsBattles} 场…" : "正在结算…";
            GUI.Label(new Rect(0, Screen.height * 0.47f, Screen.width, 30 * k), text, _bold);
        }

        void DrawPlayingOverlay(float k)
        {
            // 左上：返回配阵
            float bw = 110f * k, bh = 30f * k;
            if (GUI.Button(new Rect(12 * k, 12 * k, bw, bh), "返回配阵", _btn))
                ReturnToSetup();

            // 右上：与 BattleReportTester 同款完整播放控件
            float x = Screen.width - bw - 12f * k, y = 12f * k;
            if (GUI.Button(new Rect(x, y, bw, bh), "重播", _btn))
                ReplayLastReport();
            if (GUI.Button(new Rect(x, y + bh * 1.2f, bw, bh), "跳到结尾", _btn))
                _runner?.SkipToEnd();
            if (GUI.Button(new Rect(x, y + bh * 2.4f, bw, bh), $"速度 x{Speed:0.##}", _btn))
                Speed = Speed >= 4f ? 0.5f : Speed * 2f;
            if (_runner != null && !_runner.IsPlaying &&
                GUI.Button(new Rect(x, y + bh * 3.6f, bw, bh), "高光回放", _btn))
                _runner.PlayHighlight("A");
            if (_runner != null && !_runner.IsPlaying &&
                GUI.Button(new Rect(x, y + bh * 4.8f, bw, bh), "打开结算", _btn))
                _runner.ShowSettlement();
        }

        // ------------------------------------------------------------ 武将池

        void DrawHeroPicker(float k)
        {
            var box = ModalRect(0.72f, 0.78f, k, out float x, out float y, out float w, out float h);
            GUI.Box(box, GUIContent.none);
            _bold.fontSize = Mathf.RoundToInt(20 * k);
            GUI.Label(new Rect(x, y + 6 * k, w, 26 * k),
                $"选择武将 → {SlotTitle(_heroPickerSlot)}", _bold);

            int cols = 4;
            float cw = (w - 24 * k) / cols - 8 * k;
            float ch = 64 * k;
            int rows = Mathf.CeilToInt(_catalog.Heroes.Count / (float)cols);
            int teamStart = IsTeamA(_heroPickerSlot) ? 0 : 6;
            bool teamFull = FilledCount(teamStart) >= MaxHeroesPerTeam
                && _slots[_heroPickerSlot].IsEmpty;
            _pickerScroll = GUI.BeginScrollView(
                new Rect(x + 12 * k, y + 40 * k, w - 24 * k, h - 92 * k), _pickerScroll,
                new Rect(0, 0, w - 44 * k, rows * (ch + 8 * k)));
            for (int i = 0; i < _catalog.Heroes.Count; i++)
            {
                var hero = _catalog.Heroes[i];
                var r = new Rect(i % cols * (cw + 8 * k), i / cols * (ch + 8 * k), cw, ch);
                bool used = TemplateUsedInTeam(_heroPickerSlot, hero.TemplateId);
                GUI.enabled = !used && !teamFull;
                string label = $"{hero.Name}\n{hero.FactionName}"
                    + (used ? "（已上阵）" : teamFull ? "（已满3人）" : "");
                if (GUI.Button(r, label, _cell))
                {
                    var slot = _slots[_heroPickerSlot];
                    slot.Clear();
                    slot.TemplateId = hero.TemplateId;
                    _heroPickerSlot = -1;
                }
                GUI.enabled = true;
            }
            GUI.EndScrollView();

            if (GUI.Button(new Rect(x + w * 0.5f - 60 * k, y + h - 44 * k, 120 * k, 34 * k),
                    "取消", _btn))
                _heroPickerSlot = -1;
        }

        // ------------------------------------------------------------ 战法池

        void DrawSkillPicker(float k)
        {
            var slot = _slots[_skillPickerSlot];
            var box = ModalRect(0.6f, 0.78f, k, out float x, out float y, out float w, out float h);
            GUI.Box(box, GUIContent.none);
            _bold.fontSize = Mathf.RoundToInt(20 * k);
            GUI.Label(new Rect(x, y + 6 * k, w, 26 * k),
                $"{_catalog.HeroOf(slot.TemplateId)?.Name} · 选择战法（格 {_skillPickerCell + 1}）", _bold);

            float rowH = 40 * k;
            _pickerScroll = GUI.BeginScrollView(
                new Rect(x + 12 * k, y + 40 * k, w - 24 * k, h - 96 * k), _pickerScroll,
                new Rect(0, 0, w - 44 * k, _catalog.SkillPool.Count * (rowH + 6 * k)));
            for (int i = 0; i < _catalog.SkillPool.Count; i++)
            {
                var sk = _catalog.SkillOf(_catalog.SkillPool[i]);
                var r = new Rect(0, i * (rowH + 6 * k), w - 48 * k, rowH);
                bool used = SkillUsedOnHero(slot, sk.SkillId);
                bool chosen = _skillPickerChoice == sk.SkillId;
                GUI.enabled = !used;
                GUI.color = chosen ? new Color(0.6f, 0.9f, 1f) : Color.white;
                string label = $"{sk.Name}　[{sk.Timing}·{sk.RateText}]" + (used ? "（已装配）" : "");
                if (GUI.Button(r, label, _btn)) _skillPickerChoice = sk.SkillId;
                GUI.color = Color.white;
                GUI.enabled = true;
            }
            GUI.EndScrollView();

            GUI.enabled = _skillPickerChoice != null;
            if (GUI.Button(new Rect(x + w * 0.5f - 130 * k, y + h - 44 * k, 120 * k, 34 * k),
                    "装配", _btn))
            {
                slot.ExtraSkills[_skillPickerCell] = _skillPickerChoice;
                _skillPickerSlot = -1;
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + w * 0.5f + 10 * k, y + h - 44 * k, 120 * k, 34 * k),
                    "取消", _btn))
                _skillPickerSlot = -1;
        }

        // ------------------------------------------------------------ 详情

        void DrawHeroDetail(float k)
        {
            var slot = _slots[_heroDetailSlot];
            var hero = _catalog.HeroOf(slot.TemplateId);
            var box = ModalRect(0.44f, 0.56f, k, out float x, out float y, out float w, out float h);
            GUI.Box(box, GUIContent.none);
            _bold.fontSize = Mathf.RoundToInt(22 * k);
            GUI.Label(new Rect(x, y + 8 * k, w, 28 * k), hero.Name, _bold);

            var innate = _catalog.SkillOf(hero.InnateSkill);
            _label.fontSize = Mathf.RoundToInt(15 * k);
            string body =
                $"阵营：{hero.FactionName}　性格：{(string.IsNullOrEmpty(hero.TraitId) ? "无" : hero.TraitId)}\n" +
                $"武力 {hero.Force}　智力 {hero.Intelligence}\n" +
                $"统率 {hero.Command}　敏捷 {hero.Speed}\n（{_catalog.Level} 级面板）\n\n" +
                $"自带战法：{innate?.Name}\n[{innate?.Timing}·触发 {innate?.RateText}]" +
                (hero.HiddenSkills.Count > 0
                    ? $"\n隐藏被动：{string.Join("、", hero.HiddenSkills)}" : "");
            GUI.Label(new Rect(x + 16 * k, y + 40 * k, w - 32 * k, h - 100 * k), body, _label);

            float by = y + h - 46 * k;
            if (GUI.Button(new Rect(x + 14 * k, by, 96 * k, 34 * k), "更换", _btn))
            {
                _heroPickerSlot = _heroDetailSlot;
                _pickerScroll = Vector2.zero;
                _heroDetailSlot = -1;
            }
            if (GUI.Button(new Rect(x + w * 0.5f - 48 * k, by, 96 * k, 34 * k), "移除", _btn))
            {
                slot.Clear();
                _heroDetailSlot = -1;
            }
            if (GUI.Button(new Rect(x + w - 110 * k, by, 96 * k, 34 * k), "关闭", _btn))
                _heroDetailSlot = -1;
        }

        void DrawSkillDetail(float k)
        {
            var slot = _slots[_skillDetailSlot];
            var hero = _catalog.HeroOf(slot.TemplateId);
            bool innate = _skillDetailCell < 0;
            string skillId = innate ? hero.InnateSkill : slot.ExtraSkills[_skillDetailCell];
            var sk = _catalog.SkillOf(skillId);
            var box = ModalRect(0.42f, 0.44f, k, out float x, out float y, out float w, out float h);
            GUI.Box(box, GUIContent.none);
            _bold.fontSize = Mathf.RoundToInt(22 * k);
            GUI.Label(new Rect(x, y + 8 * k, w, 28 * k), sk?.Name ?? skillId, _bold);

            _label.fontSize = Mathf.RoundToInt(15 * k);
            string body =
                $"持有：{hero.Name}（{(innate ? "自带" : $"可配格 {_skillDetailCell + 1}")}）\n" +
                $"类型：{sk?.Timing}{(sk != null && sk.IsOracle ? "·神谕" : "")}\n" +
                $"触发率：{sk?.RateText}\n" +
                (sk != null && sk.PrepareRounds > 0 ? $"准备回合：{sk.PrepareRounds}\n" : "") +
                "\n完整效果描述见 docs/skills/ 分册";
            GUI.Label(new Rect(x + 16 * k, y + 40 * k, w - 32 * k, h - 100 * k), body, _label);

            float by = y + h - 46 * k;
            if (!innate)
            {
                if (GUI.Button(new Rect(x + 14 * k, by, 96 * k, 34 * k), "更换", _btn))
                {
                    _skillPickerSlot = _skillDetailSlot;
                    _skillPickerCell = _skillDetailCell;
                    _skillPickerChoice = null;
                    _pickerScroll = Vector2.zero;
                    _skillDetailSlot = -1;
                    _skillDetailCell = -2;
                    return;
                }
                if (GUI.Button(new Rect(x + w * 0.5f - 48 * k, by, 96 * k, 34 * k), "卸下", _btn))
                {
                    slot.ExtraSkills[_skillDetailCell] = null;
                    _skillDetailSlot = -1;
                    _skillDetailCell = -2;
                    return;
                }
            }
            if (GUI.Button(new Rect(x + w - 110 * k, by, 96 * k, 34 * k), "关闭", _btn))
            {
                _skillDetailSlot = -1;
                _skillDetailCell = -2;
            }
        }

        // ------------------------------------------------------------ 百场统计

        void DrawStatsModal(float k)
        {
            var s = _stats;
            var box = ModalRect(0.88f, 0.88f, k, out float x, out float y, out float w, out float h);
            GUI.Box(box, GUIContent.none);
            _bold.fontSize = Mathf.RoundToInt(22 * k);
            GUI.Label(new Rect(x, y + 6 * k, w, 28 * k),
                $"{s.N} 场统计（{s.ElapsedSec}s）", _bold);

            string WinText(string tid) => s.WinRate.TryGetValue(tid, out var v)
                ? $"{tid}={v.Wins}({v.RatePct}%)" : $"{tid}=0";
            _label.fontSize = Mathf.RoundToInt(15 * k);
            GUI.Label(new Rect(x, y + 36 * k, w, 22 * k),
                $"平均结束回合 {s.AvgEndRound}　胜率 {WinText("A")}  {WinText("B")}  {WinText("draw")}",
                _label);
            string TeamText(string tid) => s.Teams.TryGetValue(tid, out var t)
                ? $"{tid} 队均：死 {t.AvgDead} / 伤 {t.AvgWounded} / 余 {t.AvgRemain}" : "";
            GUI.Label(new Rect(x, y + 58 * k, w, 22 * k),
                $"{TeamText("A")}　　{TeamText("B")}", _label);

            // 左右分队技能行
            float top = y + 88 * k;
            float colW = w * 0.46f;
            float contentH = EstimateStatsHeight(k);
            _statsScroll = GUI.BeginScrollView(
                new Rect(x + 10 * k, top, w - 20 * k, h - 140 * k), _statsScroll,
                new Rect(0, 0, w - 44 * k, contentH));
            float ay = 0, by2 = 0;
            var heroStyle = _bold;
            foreach (var hero in s.Heroes)
            {
                bool left = hero.Team == "A";
                float cx = left ? 0 : colW + 24 * k;
                float cy = left ? ay : by2;
                heroStyle.fontSize = Mathf.RoundToInt(16 * k);
                heroStyle.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(cx, cy, colW, 20 * k), $"[{hero.Team}] {hero.HeroId}", heroStyle);
                cy += 22 * k;
                foreach (var row in hero.Rows)
                {
                    GUI.Label(new Rect(cx + 12 * k, cy, colW, 18 * k),
                        $"{row.Name}  均释放 {row.AvgTriggers}  均伤害 {row.AvgDamage}",
                        GUI.skin.label);
                    cy += 18 * k;
                }
                cy += 8 * k;
                if (left) ay = cy; else by2 = cy;
            }
            heroStyle.alignment = TextAnchor.MiddleCenter;
            GUI.EndScrollView();

            if (GUI.Button(new Rect(x + w * 0.5f - 60 * k, y + h - 44 * k, 120 * k, 34 * k),
                    "关闭", _btn))
                _stats = null;
        }

        float EstimateStatsHeight(float k)
        {
            float a = 0, b = 0;
            foreach (var hero in _stats.Heroes)
            {
                float rows = 30 * k + hero.Rows.Count * 18 * k;
                if (hero.Team == "A") a += rows; else b += rows;
            }
            return Mathf.Max(a, b) + 30 * k;
        }

        // ------------------------------------------------------------ 工具

        static Rect ModalRect(float wr, float hr, float k,
            out float x, out float y, out float w, out float h)
        {
            w = Screen.width * wr;
            h = Screen.height * hr;
            x = (Screen.width - w) * 0.5f;
            y = (Screen.height - h) * 0.5f;
            return new Rect(x, y, w, h);
        }
    }
}
