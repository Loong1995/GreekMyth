using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ClientBattle.Events;
using ClientBattle.Units;
using ClientBattle.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClientBattle.Test
{
    // =========================================================================
    // 特效审核画廊：把**全项目可用特效**（我方标准件 + 各厂包）放到
    // **真实舞台 + 真实卡牌**上逐件过，并就地标记「可用 / 否决」。
    // 入口：菜单 GreekMyth/特效/特效画廊（一键）。
    //
    // 为什么不用厂包自带预览：那里判的是"这效果本身好不好看"；这里判的是
    // "接进我们舞台后好不好用" —— 亮色大理石地面、竖直卡牌立绘、55° 俯视，
    // 同一个特效两处观感能差很远（尺寸、排序、亮度都可能翻车）。
    //
    // 舞台与卡牌用真战报建（走 BattleBoardView.Build），但**不播战报本身**。
    // 我方件走 VFXManager 池化路径（连排序规则一起验）；厂包件直接实例化，
    // 只补一次排序抬升，否则会被地面/卡牌盖住看不见。
    // =========================================================================

    public sealed class VfxGalleryRunner : MonoBehaviour
    {
        [Serializable]
        public class Group
        {
            public string Name = "";
            /// <summary>true = 我方 Resources 标准件（走 VFXManager）。</summary>
            public bool Ours;
            public List<GameObject> Items = new List<GameObject>();
        }

        public string ReportPath = "battle_reports/manual_3v3_seed20260722.json";
        public float AutoRespawnSeconds = 2.6f;
        [SerializeField] List<Group> Groups = new List<Group>();

        const string MarkFile = "vfx_audit_marks.txt";

        enum Anchor
        {
            CardBody = 0,    // 卡牌身上（光环/命中类）
            CardFoot = 1,    // 卡牌接地中心（脚下类）
            GroundFlat = 2,  // 卡牌脚下并**平躺**（判能否当地面法阵/贴花用）
            BoardCenter = 3, // 棋盘中心（群攻落点/全局类）
            Ballistic = 4,   // 弹道：从施法者定位圆心射向敌方卡的定位圆心
            Shroud = 5,      // 罩身：切面＝卡牌定位圆、顶部＝卡牌上边缘（VfxShroudFitter）
        }

        const int AnchorCount = 6;

        /// <summary>默认按【罩身】审核的件（文件名**全等**，忽略大小写）。
        /// 这类件是"从脚下升起把卡罩住"的立体件，用其它锚点看必然误判。
        /// 全等是为了不把 `Effect31_Collision` 命中碎件误判成罩身。</summary>
        static readonly string[] ShroudKeys = { "effect31" };

        /// <summary>命中碎件后缀。厂包主件撞到碰撞体后 Instantiate 出来的那一截，
        /// 本身只有 1~2 秒的 burst（闪电/碎石），不是常驻罩。画廊默认贴脚下、
        /// 加快重播，否则一轮闪过就空，审核会误判为"没效果"。</summary>
        const string CollisionSuffix = "_collision";

        /// <summary>命中碎件的自动重播间隔（秒）。比常规 2.6 短，保证审核时
        /// 几乎一直在闪，看得见那一击。</summary>
        const float CollisionRespawnSeconds = 1.35f;

        /// <summary>弹道件飞完全程的目标时长（秒）。用它反算 Speed，
        /// 免得吃厂包默认值（RFX1 默认 Speed=1 / Distance=30，在我们 8 米半径的
        /// 逻辑圆里等于"慢慢飞出舞台再也不回来"）。</summary>
        const float TravelSeconds = 0.9f;

        readonly List<UnitView> _units = new List<UnitView>();
        readonly Dictionary<string, string> _usage = new Dictionary<string, string>();
        readonly List<string> _picked = new List<string>();
        readonly List<string> _rejected = new List<string>();
        readonly HashSet<GameObject> _lifted = new HashSet<GameObject>();

        const int RingSegments = 72;

        BattleBoardView _board;
        VFXManager _vfx;
        LineRenderer _ring;
        GameObject _targetMarker;
        bool _fitCircle;
        bool _autoBallistic = true;
        bool _currentBallistic;
        bool _slowMo;
        /// <summary>上次自动切锚点用的件名，避免手动按 F 后又被自动改回去。</summary>
        string _autoAnchorKey = "";
        int _group;
        int _index;
        int _unitIndex;
        float _scaleMul = 1f;
        Anchor _anchor = Anchor.CardBody;
        bool _autoLoop = true;
        float _respawnAt;
        GameObject _current;
        string _currentKey = "";
        bool _currentPooled;
        string _status = "载入中…";

        public void SetGroups(List<Group> groups) => Groups = groups ?? new List<Group>();

        Group Cur => Groups.Count > 0 ? Groups[Mathf.Clamp(_group, 0, Groups.Count - 1)] : null;

        IEnumerator Start()
        {
            CameraFitter.EnsureOn(Camera.main);
            BattlePostFx.Ensure();

            if (!BuildStage()) yield break;

            _vfx = VFXManager.Ensure();
            _vfx.Prewarm();
            while (!_vfx.PrewarmComplete) yield return null;

            EnsureOwnGroup();
            BuildUsageIndex();
            if (Cur == null || Cur.Items.Count == 0)
            {
                _status = "没有可展示的 prefab（用菜单 GreekMyth/特效/特效画廊 启动）";
                yield break;
            }
            ApplyGroupDefaults();
            Spawn();
        }

        bool BuildStage()
        {
            string full = Path.Combine(Application.streamingAssetsPath, ReportPath);
            if (!File.Exists(full))
            {
                _status = "找不到战报：" + full;
                Debug.LogError("[VfxGallery] " + _status);
                return false;
            }

            var report = BattleReport.Parse(File.ReadAllText(full));
            RepairSlots(report);
            _board = FindFirstObjectByType<BattleBoardView>();
            if (_board == null) _board = new GameObject("BattleBoard").AddComponent<BattleBoardView>();
            _board.Build(report); // 只建场，不播战报

            _units.Clear();
            _units.AddRange(_board.AllUnits.OrderBy(u => u.transform.position.x));
            return true;
        }

        /// <summary>把同队重号的站位摊开到雁行阵 1/2/6。
        /// 旧战报导出的 Position 是 0/1/2 的**下标**而不是 1~6 的**格号**，
        /// 直接建场会两张卡压同一格挡住特效。画廊只要一个有代表性的舞台。</summary>
        static void RepairSlots(BattleReport report)
        {
            int[] canonical = { 1, 2, 6 };
            foreach (var team in report.Teams)
            {
                var seen = new HashSet<int>();
                if (team.Heroes.All(h => seen.Add(StanceLayout.Normalize(h.Position)))) continue;
                for (int i = 0; i < team.Heroes.Count; i++)
                    team.Heroes[i].Position = canonical[i % canonical.Length];
            }
        }

        /// <summary>我方标准件不经编辑器注入也能自己找到（在 Resources 下）。</summary>
        void EnsureOwnGroup()
        {
            if (Groups.Any(g => g.Ours)) return;
            var ours = new Group { Name = "我方标准件", Ours = true };
            ours.Items.AddRange(Resources.LoadAll<GameObject>("ClientBattle/VFX")
                .Where(p => p != null).OrderBy(p => p.name, StringComparer.Ordinal));
            Groups.Insert(0, ours);
        }

        /// <summary>反查每个 key 现在挂在哪些战法/状态上。反射遍历 PerformanceProfile
        /// 的全部 string 字段，以后新增 key 字段不用回来改这里。</summary>
        void BuildUsageIndex()
        {
            _usage.Clear();
            var db = PerformanceDatabase.LoadOrDefault();
            if (db == null) return;

            var fields = typeof(PerformanceProfile)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(string) && f.Name != "SkillOrStatusId")
                .ToArray();

            void Scan(PerformanceProfile p, string owner)
            {
                if (p == null) return;
                foreach (var f in fields)
                {
                    string key = f.GetValue(p) as string;
                    if (string.IsNullOrEmpty(key)) continue;
                    string entry = $"{owner}·{f.Name.Replace("Key", "")}";
                    _usage[key] = _usage.TryGetValue(key, out var prev) ? prev + " / " + entry : entry;
                }
            }

            foreach (var p in db.SpecialProfiles)
                Scan(p, string.IsNullOrEmpty(p.SkillOrStatusId) ? "(组默认)" : p.SkillOrStatusId);
            Scan(db.ActiveDefault, "默认·主动");
            Scan(db.MeleeDefault, "默认·普攻");
            Scan(db.PursuitDefault, "默认·追击");
            Scan(db.StatusTriggerDefault, "默认·状态触发");
            Scan(db.OracleDefault, "默认·神谕");
            Scan(db.GlobalDefault, "默认·兜底");
        }

        void Update()
        {
            if (Cur == null || Cur.Items.Count == 0) return;

            // 单一输入通道（勿再叠 OnGUI 的按键轮询，否则同帧走两步）。
            // OnGUI **按钮**可以调 Step*：那是点击，不会和键盘同帧抢。
            //
            // Tab 在编辑器里经常被抢走（焦点跳 Inspector），所以切包另备 [ ]。
            // Game 未聚焦时 Keyboard.current 为 null，回退旧 Input。
            if (Pressed(Key.LeftArrow, KeyCode.LeftArrow)) StepItem(-1);
            if (Pressed(Key.RightArrow, KeyCode.RightArrow)) StepItem(1);
            if (Pressed(Key.PageDown, KeyCode.PageDown)) StepItem(10);
            if (Pressed(Key.PageUp, KeyCode.PageUp)) StepItem(-10);
            if (Pressed(Key.UpArrow, KeyCode.UpArrow) || Pressed(Key.LeftBracket, KeyCode.LeftBracket))
                StepGroup(-1);
            if (Pressed(Key.DownArrow, KeyCode.DownArrow) || Pressed(Key.Tab, KeyCode.Tab)
                || Pressed(Key.RightBracket, KeyCode.RightBracket))
                StepGroup(1);
            if (Pressed(Key.R, KeyCode.R) || Pressed(Key.Space, KeyCode.Space)) Spawn();
            if (Pressed(Key.F, KeyCode.F))
            {
                _anchor = (Anchor)(((int)_anchor + 1) % AnchorCount);
                _autoAnchorKey = _currentKey; // 手动挑过的件不再被自动锚点覆盖
                Spawn();
            }
            if (Pressed(Key.B, KeyCode.B)) { _autoBallistic = !_autoBallistic; Spawn(); }
            if (Pressed(Key.T, KeyCode.T)) CycleUnit();
            if (Pressed(Key.G, KeyCode.G)) { _autoLoop = !_autoLoop; Refresh(); }
            if (Pressed(Key.Minus, KeyCode.Minus)) { _scaleMul *= 0.8f; Spawn(); }
            if (Pressed(Key.Equals, KeyCode.Equals)) { _scaleMul *= 1.25f; Spawn(); }
            if (Pressed(Key.Digit0, KeyCode.Alpha0)) { _scaleMul = 1f; Spawn(); }
            if (Pressed(Key.C, KeyCode.C)) { _fitCircle = !_fitCircle; Spawn(); }
            if (Pressed(Key.M, KeyCode.M)) Mark(_picked, _rejected, "可用");
            if (Pressed(Key.N, KeyCode.N)) Mark(_rejected, _picked, "否决");
            if (Pressed(Key.P, KeyCode.P)) DumpMarks();
            if (Pressed(Key.K, KeyCode.K)) ToggleSlowMo();
            if (Pressed(Key.J, KeyCode.J)) ToggleDepthProxy();

            if (_currentBallistic) LiftPackSpawns();
            if (_autoLoop && Time.unscaledTime >= _respawnAt) Spawn();
        }

        /// <summary>新 Input System 优先；Game 未聚焦或 Keyboard 为空时回退旧 Input。
        /// 两套只取其一，避免同帧双触发。</summary>
        static bool Pressed(Key newKey, KeyCode legacy)
        {
            var kb = Keyboard.current;
            if (kb != null) return kb[newKey].wasPressedThisFrame;
            return Input.GetKeyDown(legacy);
        }

        /// <summary>慢放。厂包出手件整段只有 0.9 秒，正常速度下"闪一下就没了"，
        /// 判不出弹道形态与命中衔接；审核这类件基本必开。</summary>
        void ToggleSlowMo()
        {
            _slowMo = !_slowMo;
            Time.timeScale = _slowMo ? 0.25f : 1f;
            Refresh();
        }

        /// <summary>卡牌深度代理开关（A/B 对比）。关掉后卡牌退回"纯透明 Sprite"状态：
        /// 折射壳会把卡抹掉、护盾穹顶前后半塌成一片、软粒子硬边穿插 ——
        /// 判"这件到底适不适配"时先按 J 看一眼两边差别。</summary>
        void ToggleDepthProxy()
        {
            CardDepthProxy.SetEnabled(!CardDepthProxy.Enabled);
            Refresh();
        }

        void OnDisable() => Time.timeScale = 1f;

        void StepItem(int delta)
        {
            int n = Cur.Items.Count;
            _index = ((_index + delta) % n + n) % n;
            Spawn();
        }

        void StepGroup(int delta)
        {
            if (Groups.Count == 0) return;
            _group = ((_group + delta) % Groups.Count + Groups.Count) % Groups.Count;
            _index = 0;
            _autoAnchorKey = ""; // 换包后允许按新包规则自动锚点
            // 大包第一件 Instantiate 可能卡半秒，先改 HUD 让人知道按键生效了。
            _status = $"切换到包 [{_group + 1}/{Groups.Count}] {Cur?.Name}…";
            ApplyGroupDefaults();
            Spawn();
        }

        /// <summary>换包时切到该包的默认审核姿势。厂包件一律先按**地面 + 卡牌定位圆**
        /// 看（它们是 3D 世界尺度的散件，贴卡看不出可用性，且多数是落地/冲击型）；
        /// 我方标准件已按 key 接线调好，默认贴卡看。</summary>
        void ApplyGroupDefaults()
        {
            if (Cur == null) return;
            if (Cur.Ours)
            {
                _anchor = Anchor.CardBody;
                _fitCircle = false;
            }
            else
            {
                _anchor = Anchor.CardFoot;
                _fitCircle = true;
                int athena = _units.FindIndex(u => u.name.Contains("雅典娜"));
                if (athena >= 0) _unitIndex = athena;
            }
            _scaleMul = 1f;
        }

        void CycleUnit()
        {
            if (_units.Count == 0) return;
            _unitIndex = (_unitIndex + 1) % _units.Count;
            Spawn();
        }

        // ------------------------------------------------------------ 播放

        void Spawn()
        {
            Despawn();

            var prefab = Cur.Items[Mathf.Clamp(_index, 0, Cur.Items.Count - 1)];
            if (prefab == null)
            {
                _status = $"[{_group + 1}/{Groups.Count}] {Cur.Name}  第 {_index + 1} 件是空槽";
                _respawnAt = Time.unscaledTime + AutoRespawnSeconds;
                return;
            }

            _currentKey = prefab.name;
            _currentPooled = Cur.Ours;
            if (_autoAnchorKey != _currentKey)
            {
                if (IsShroud(_currentKey))
                {
                    _anchor = Anchor.Shroud;
                    _autoAnchorKey = _currentKey;
                }
                else if (IsCollisionPart(_currentKey))
                {
                    // 命中碎件原点是爆点，贴脚下才读得对；贴卡身会被卡面挡住大半。
                    _anchor = Anchor.CardFoot;
                    _fitCircle = true;
                    _autoAnchorKey = _currentKey;
                }
            }
            float loopSec = IsCollisionPart(_currentKey) ? CollisionRespawnSeconds : AutoRespawnSeconds;
            float duration = loopSec - 0.15f;
            var unit = _units.Count > 0 ? _units[Mathf.Clamp(_unitIndex, 0, _units.Count - 1)] : null;

            // 自带位移的厂包主件必须走弹道，否则它沿自己的 local forward 飞出舞台，
            // 且命中件永远不生成 —— 那是"件不会显示"的头号原因，不是件不可用。
            var foe = FoeOf(unit);
            _currentBallistic = !_currentPooled && foe != null && unit != null
                && (_anchor == Anchor.Ballistic
                    || (_autoBallistic && _anchor != Anchor.Shroud && HasMotion(prefab)));

            // 弹道从卡身出、打卡身：卡牌碰撞盒在卡面高度（VfxCollisionStage），
            // 贴地平飞会从盒下方擦过打到地面，读作"打偏了"。
            Vector3 pos = _currentBallistic ? unit.transform.position : AnchorPosition(unit);

            if (_currentPooled)
            {
                _current = _anchor == Anchor.CardBody && unit != null
                    ? _vfx.PlayOn(_currentKey, unit.transform, duration)
                    : _vfx.PlayAt(_currentKey, pos, duration);
            }
            else
            {
                // 厂包件常带自家控制脚本，个别件在 OnEnable 里就抛（例：RFX1 曲线脚本
                // 未初始化 props）。审核台不能被单件带崩，抓下来记成状态继续翻。
                try
                {
                    _current = Instantiate(prefab, pos, Quaternion.identity);
                    _current.name = prefab.name + "(Gallery)";
                    if (_anchor == Anchor.CardBody && unit != null) _current.transform.SetParent(unit.transform, true);
                    LiftSorting(_current);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VfxGallery] {prefab.name} 实例化报错：{e.Message}");
                }
            }

            if (_current != null)
            {
                if (_currentBallistic)
                {
                    AimBallistic(_current, pos, foe.transform.position);
                }
                else if (_anchor == Anchor.Shroud && unit != null)
                {
                    // 罩身件的定径/定位是**规格**而非审核偏好，不受 C 键影响
                    VfxShroudFitter.Fit(_current, unit.RestPosition);
                }
                else
                {
                    if (_anchor == Anchor.GroundFlat)
                        _current.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    if (_fitCircle) FitToCardCircle(_current);
                }

                if (!Mathf.Approximately(_scaleMul, 1f))
                    _current.transform.localScale *= _scaleMul;
                if (!_currentBallistic && _anchor is Anchor.CardFoot or Anchor.GroundFlat)
                    RescueIfBuried(_current);
                if (!_currentPooled) ForcePlay(_current);
            }

            UpdateCircleRing(unit);

            _respawnAt = Time.unscaledTime + loopSec;
            Refresh();
        }

        void Despawn()
        {
            SweepPackSpawns();
            if (_current == null) return;
            if (_currentPooled) _vfx.Release(_currentKey, _current);
            else Destroy(_current);
            _current = null;
        }

        /// <summary>清掉厂包脚本自己生成的命中件。它们是**场景根节点**（RFX1 按
        /// `CollisionEffectInWorldSpace` 直接 Instantiate，不挂在弹道下），不清就会
        /// 一轮一轮堆积。判据用 "(Clone)"：我方件全走 VFXManager 池化，不会有这后缀。</summary>
        void SweepPackSpawns()
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go == null || go.transform.parent != null) continue;
                if (go.name.EndsWith("(Clone)")) Destroy(go);
            }
            _lifted.Clear();
        }

        /// <summary>厂包自己生成的命中件也要补排序抬升，否则会被卡牌盖住 ——
        /// 它没经过我们的 Spawn，只能在这里逮住。</summary>
        void LiftPackSpawns()
        {
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (go == null || go.transform.parent != null) continue;
                if (!go.name.EndsWith("(Clone)")) continue;
                if (!_lifted.Add(go)) continue;
                LiftSorting(go);
            }
        }

        Vector3 AnchorPosition(UnitView unit)
        {
            if (unit == null) return _board.Center;
            return _anchor switch
            {
                Anchor.CardBody => unit.transform.position,
                Anchor.CardFoot or Anchor.GroundFlat => ArenaSlotLayout.GroundFoot(unit.RestPosition),
                Anchor.Shroud => ArenaSlotLayout.CardCircleCenter(unit.RestPosition),
                _ => _board.Center,
            };
        }

        // ------------------------------------------------------------ 弹道模式
        //
        // 厂包主件（Magic Pack 的 `Prefabs/Effects/EffectN`、RFX4 的 `EffectN`）不是
        // "放在一点上播"的散件，而是**一整套出手流程**：自己带位移脚本飞出去，撞到
        // 碰撞体时再生成它的命中件（`EffectsOnCollision`）。单锚点摆放下这类件必然
        // 演不出来，于是被误判为"标准化不出可用组件"。审核台必须给它两点：
        // 施法者的卡牌定位圆心 → 敌方卡的卡牌定位圆心。
        //
        // 三件事缺一不可：
        //   1) 朝向 —— Target 为空时 RFX1 沿自己的 local forward 飞，identity 旋转
        //      就是朝屏幕深处冲出舞台；
        //   2) Target/射程 —— 默认 Distance=30 远超逻辑圆半径 8，Speed=1 又极慢；
        //   3) 落点有碰撞体 —— 命中件只在 raycast 命中分支生成，而我们的地面是
        //      特意去掉碰撞体的底图（ArenaStageView），全场本来一个碰撞体都没有。

        /// <summary>是否是自带位移的主件（据此自动切弹道）。判组件类型名而不判文件名：
        /// 厂包命名不统一，但位移一定挂在 *TransformMotion / *PhysicsMotion 上。</summary>
        static bool HasMotion(GameObject prefab)
        {
            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n.Contains("TransformMotion") || n.Contains("PhysicsMotion")) return true;
            }
            return false;
        }

        /// <summary>给弹道件对准目标：朝向 + Target + 射程/速度 + 落点碰撞体。</summary>
        void AimBallistic(GameObject instance, Vector3 from, Vector3 to)
        {
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float dist = flat.magnitude;
            if (dist < 0.01f) return;

            instance.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            WireMotion(instance, EnsureTargetMarker(to), dist);
        }

        /// <summary>落点标记：只当厂包位移脚本的 Target（朝哪飞）。
        /// 命中判定不再靠它 —— 卡牌自己带了碰撞盒（VfxCollisionStage），
        /// 弹体撞在真卡面上，命中件才落在"人"身上而不是一个隐形球上。</summary>
        GameObject EnsureTargetMarker(Vector3 pos)
        {
            if (_targetMarker == null) _targetMarker = new GameObject("BallisticTarget");
            _targetMarker.transform.position = pos;
            return _targetMarker;
        }

        /// <summary>反射写厂包位移脚本的参数。ClientBattle 是独立 asmdef，引用不到
        /// Assembly-CSharp 里的厂包类型，只能按字段名写；按名字而非类型名匹配，
        /// 新包沿用同一套命名（Target/Distance/Speed）时无需回来改。</summary>
        static void WireMotion(GameObject instance, GameObject target, float dist)
        {
            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var type = mb.GetType();
                if (!type.Name.StartsWith("RFX")) continue; // 只碰厂包脚本

                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                bool isMotion = false;
                foreach (var f in fields)
                    if (f.Name is "Distance" or "MaxDistnace" or "Target") { isMotion = true; break; }
                if (!isMotion) continue;

                foreach (var f in fields)
                {
                    switch (f.Name)
                    {
                        case "Target" when f.FieldType == typeof(GameObject):
                            f.SetValue(mb, target); break;
                        case "Target" when f.FieldType == typeof(Transform):
                            f.SetValue(mb, target.transform); break;
                        case "Distance" when f.FieldType == typeof(float):
                        case "MaxDistnace" when f.FieldType == typeof(float): // 厂包原拼写
                            f.SetValue(mb, dist); break;
                        case "LimitMaxDistance" when f.FieldType == typeof(bool):
                            f.SetValue(mb, true); break;
                        case "Speed" when f.FieldType == typeof(float):
                            f.SetValue(mb, dist / TravelSeconds); break;
                    }
                }
            }
        }

        /// <summary>取对手卡（弹道落点）：优先同列最近的敌方卡，保证弹道横穿中线可见。</summary>
        UnitView FoeOf(UnitView unit)
        {
            if (unit == null) return null;
            UnitView best = null;
            float bestDx = float.MaxValue;
            foreach (var u in _units)
            {
                if (u == null || u == unit || u.TeamId == unit.TeamId) continue;
                float dx = Mathf.Abs(u.RestPosition.x - unit.RestPosition.x);
                if (dx < bestDx) { bestDx = dx; best = u; }
            }
            return best;
        }

        /// <summary>很多厂包件的粒子是 `playOnAwake=false`（等它自己的控制脚本或
        /// 示例场景来触发），直接实例化后一片空白 —— 「彩色系列」整包 132 件都是
        /// 这样。审核台必须自己起播。</summary>
        /// <summary>厂包件常关 playOnAwake；强制起播。对 burst 命中件再推 0.05s，
        /// 避免审核切到下一件的第一帧包围盒还是空的（读作"没效果"）。</summary>
        static void ForcePlay(GameObject instance)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.parent != instance.transform && ps.transform != instance.transform)
                    continue; // 根级 Play(withChildren) 会带上子级，避免重复触发
                ps.Clear(true);
                ps.Play(true);
            }
            if (IsCollisionPart(instance.name))
            {
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Simulate(0.05f, true, false);
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps.transform.parent != instance.transform && ps.transform != instance.transform)
                        continue;
                    ps.Play(true);
                }
            }
        }

        /// <summary>按【卡牌定位圆】定径：把实例的地面投影尺寸缩到圆直径（＝卡宽）。
        /// 厂包件按 3D 世界尺度做（动辄 5~10 米），不定径就会糊满全屏没法判。
        ///
        /// 量的是**起手核心**：Simulate 只推 0.12s。推久了冲击件的碎屑已飞散开，
        /// 包围盒是"整场余波"的尺寸，据此定径会把主体缩到看不见（曾缩到 ×0.13）。
        /// 同理给下限兜底 —— 审核台宁可略溢出，也不能缩成一个点。</summary>
        static void FitToCardCircle(GameObject instance)
        {
            if (MeasureCore(instance, out var bounds))
            {
                float extent = Mathf.Max(bounds.size.x, bounds.size.z);
                if (extent > 0.001f)
                {
                    float k = Mathf.Clamp(ArenaSlotLayout.CardCircleDiameter / extent, 0.25f, 20f);
                    instance.transform.localScale *= k;
                }
            }
            Restart(instance);
        }

        /// <summary>量「起手核心」的包围盒：Simulate 只推 0.12s。
        /// 推久了冲击件的碎屑已飞散，量到的是"整场余波"，据此定径会把主体缩到
        /// 看不见（曾缩到 ×0.13）。量完必须 Restart 把粒子恢复到起播状态。</summary>
        static bool MeasureCore(GameObject instance, out Bounds bounds)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Simulate(0.12f, true, true);

            bounds = default;
            bool any = false;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        static void Restart(GameObject instance)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        /// <summary>只救「整件都在地面以下」的极端件，抬到刚露出地面。
        ///
        /// 不做"内容底面对齐地面"：厂包冲击件的原点是**爆点**、内容绕原点上下对称
        /// （Effect10_Collision 上下各 8 世界单位），对齐底面等于把爆点抬到半空，
        /// 读作"在空中炸"而不是"炸在脚下"。爆点落在定位圆心、下半截被不透明地面
        /// 挡住，才是命中该有的样子（实测如此）。
        ///
        /// 抬的是特效，不是圆：定位圆的圆心与半径始终直取 ArenaSlotLayout，不补偿。</summary>
        static void RescueIfBuried(GameObject instance)
        {
            if (MeasureCore(instance, out var bounds))
            {
                float groundY = instance.transform.position.y;
                if (bounds.max.y < groundY)
                    instance.transform.position += Vector3.up * (groundY - bounds.max.y + bounds.size.y * 0.5f);
            }
            Restart(instance);
        }

        /// <summary>把卡牌定位圆画出来，否则「有没有对准圆心、有没有溢出」全靠猜。
        ///
        /// 带子必须**躺在地面平面里**：LineRenderer 默认 `alignment=View` 会让每段
        /// 朝相机竖起来，55° 俯角下这圈线是斜立的，读起来就"不像画在地上"（透视不对）。
        /// 故把物体绕 X 转 90°（本地 XY 面 → 世界水平面）、用本地坐标下环，
        /// 并改 `TransformZ` 对齐（本地 Z ＝ 世界向上）。圆心/半径一律直取
        /// ArenaSlotLayout，此处不做任何额外补偿。</summary>
        void UpdateCircleRing(UnitView unit)
        {
            bool show = unit != null
                && _anchor is Anchor.CardFoot or Anchor.GroundFlat or Anchor.Shroud;
            if (_ring == null)
            {
                var go = new GameObject("CardCircleRing");
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                _ring = go.AddComponent<LineRenderer>();
                _ring.useWorldSpace = false;
                _ring.alignment = LineAlignment.TransformZ;
                _ring.loop = true;
                _ring.widthMultiplier = 0.04f;
                _ring.material = new Material(Shader.Find("Sprites/Default"));
                _ring.startColor = _ring.endColor = new Color(0.35f, 0.95f, 1f, 0.9f);
                _ring.sortingOrder = 44; // 地面之上、卡牌之下
                _ring.positionCount = RingSegments;
            }

            _ring.enabled = show;
            if (!show) return;

            _ring.transform.position = ArenaSlotLayout.CardCircleCenter(unit.RestPosition);
            float radius = ArenaSlotLayout.CardCircleRadius;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                _ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        /// <summary>厂包件不过 VFXManager，这里补同样的排序抬升，否则 2D 排序体系下
        /// 会被地面/卡牌盖住，看不见就无法审核。</summary>
        static void LiftSorting(GameObject instance)
        {
            if (instance.GetComponent<VfxGroundLayer>() != null) return;
            const int minOrder = 45;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SpriteMask) continue;
                if (r.sortingOrder < minOrder) r.sortingOrder = minOrder;
            }
        }

        // ------------------------------------------------------------ 审核标记

        void Mark(List<string> into, List<string> outOf, string label)
        {
            string id = $"{Cur.Name}/{_currentKey}";
            outOf.Remove(id);
            if (!into.Contains(id)) into.Add(id);
            Refresh();
            Debug.Log($"[VfxGallery] {label}：{id}（可用 {_picked.Count} / 否决 {_rejected.Count}）");
        }

        /// <summary>标记落盘，免得 Play 一停就白过一轮。</summary>
        void DumpMarks()
        {
            var sb = new StringBuilder($"# 特效审核标记 {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            sb.AppendLine($"\n## 可用（{_picked.Count}）");
            foreach (var id in _picked) sb.AppendLine("  " + id);
            sb.AppendLine($"\n## 否决（{_rejected.Count}）");
            foreach (var id in _rejected) sb.AppendLine("  " + id);

            string path = Path.Combine(Application.dataPath, "..", "Temp", MarkFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[VfxGallery] 标记已写入 Temp/{MarkFile}\n{sb}");
        }

        // ------------------------------------------------------------ HUD

        void Refresh()
        {
            string id = $"{Cur.Name}/{_currentKey}";
            string mark = _picked.Contains(id) ? "【可用】" : _rejected.Contains(id) ? "【否决】" : "";
            string usage = Cur.Ours
                ? (_usage.TryGetValue(_currentKey, out var u) ? u : "（暂未接线）")
                : "厂包原件（未接线）";
            string unitName = _units.Count > 0
                ? _units[Mathf.Clamp(_unitIndex, 0, _units.Count - 1)].name
                : "无卡牌";
            string warn = WarnOf(_current);

            var unit = _units.Count > 0 ? _units[Mathf.Clamp(_unitIndex, 0, _units.Count - 1)] : null;
            var foe = FoeOf(unit);
            string mode = _currentBallistic
                ? $"弹道 {unitName} → {(foe != null ? foe.name : "?")}"
                : $"{AnchorName(_anchor)} @ {unitName}";

            _status =
                $"包 [{_group + 1}/{Groups.Count}] {Cur.Name}    件 [{_index + 1}/{Cur.Items.Count}]  " +
                $"{_currentKey} {mark}\n" +
                $"接线：{usage}{(string.IsNullOrEmpty(warn) ? "" : "    ⚠ " + warn)}\n" +
                $"当前：{mode}    锚点 {AnchorName(_anchor)}（F）  目标卡（T）  " +
                $"自动弹道 {(_autoBallistic ? "开" : "关")}（B）  " +
                $"定位圆定径 {(_fitCircle ? "开" : "关")}（C）  ×{_scaleMul:0.00}（- = 0）  " +
                $"重播 {(_autoLoop ? "开" : "关")}（G）  慢放 {(_slowMo ? "0.25×" : "关")}（K）  " +
                $"卡牌深度 {(CardDepthProxy.Enabled ? "开" : "关")}（J）\n" +
                $"←→ 切件（PgUp/PgDn ±10）   [/] 或 ↑↓ 切包   R 重播   " +
                $"M 记可用 / N 记否决 / P 导出（先点 Game 窗口；也可用下方按钮）";
        }

        /// <summary>就地体检：标出**层**级风险，不是整件判死刑。
        /// 厂包一件里常混着可用粒子 + 不可用贴花；警告只点名问题层，接件时摘掉即可。</summary>
        static string WarnOf(GameObject instance)
        {
            if (instance == null) return "";
            var flags = new List<string>();
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                if (shader.name.EndsWith("/Decal") && !flags.Contains("含贴花层（接件时摘掉；厂包投影贴花 URP 画不出）"))
                    flags.Add("含贴花层（接件时摘掉；厂包投影贴花 URP 画不出）");
                if (shader.name.Contains("Distortion") && !flags.Contains("含屏幕扭曲层（移动端已开不透明贴图，待真机验收）"))
                    flags.Add("含屏幕扭曲层（移动端已开不透明贴图，待真机验收）");
                if (shader.name == "Hidden/InternalErrorShader" && !flags.Contains("品红/错误 shader"))
                    flags.Add("品红/错误 shader");
            }
            if (IsCollisionPart(instance.name) && !flags.Contains("命中碎件（1~2 秒一闪，非常驻）"))
                flags.Add("命中碎件（1~2 秒一闪，非常驻）");
            return string.Join("；", flags);
        }

        static string AnchorName(Anchor a) => a switch
        {
            Anchor.CardBody => "卡牌身上",
            Anchor.CardFoot => "卡牌脚下",
            Anchor.GroundFlat => "脚下平躺",
            Anchor.Ballistic => "弹道→敌卡",
            Anchor.Shroud => "罩身（等比·切面=定位圆·底面坐地）",
            _ => "棋盘中心",
        };

        /// <summary>件名是否命中罩身名单。</summary>
        static bool IsShroud(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string lower = key.ToLowerInvariant();
            foreach (var k in ShroudKeys)
                if (lower == k) return true; // 全等：碎件（EffectN_Collision）不算罩身
            return false;
        }

        /// <summary>是否厂包命中碎件（`EffectN_Collision` / 带 Gallery 后缀亦可）。</summary>
        static bool IsCollisionPart(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            string lower = key.ToLowerInvariant();
            int cut = lower.IndexOf("(gallery)", System.StringComparison.Ordinal);
            if (cut > 0) lower = lower.Substring(0, cut);
            return lower.EndsWith(CollisionSuffix);
        }

        void OnGUI()
        {
            const int pad = 12;
            var rect = new Rect(pad, pad, 1180, 104);
            GUI.Box(rect, "");
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12), _status);

            // 可点按钮：编辑器里 Tab/方向键常被抢走，点按钮最稳。
            float by = rect.yMax + 6;
            float bh = 28f;
            float bw = 88f;
            float gap = 6f;
            float x = pad;
            if (GUI.Button(new Rect(x, by, bw, bh), "上一包 [")) StepGroup(-1);
            x += bw + gap;
            if (GUI.Button(new Rect(x, by, bw, bh), "下一包 ]")) StepGroup(1);
            x += bw + gap;
            if (GUI.Button(new Rect(x, by, bw, bh), "上一件 ←")) StepItem(-1);
            x += bw + gap;
            if (GUI.Button(new Rect(x, by, bw, bh), "下一件 →")) StepItem(1);
            x += bw + gap;
            if (GUI.Button(new Rect(x, by, bw, bh), "重播 R")) Spawn();
        }
    }
}
