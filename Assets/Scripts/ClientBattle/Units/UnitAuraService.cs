using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 常驻光环服务（client_perform §二 神谕表演的核心机制）：
    // 「我方武将被施加雷霆神谕时，身上环绕随机闪电特效」——即：
    //   状态施加 → 挂循环光环特效在卡牌上；状态移除/整局重置/阵亡 → 撤下。
    //
    // 数据源：StatusPresentationRegistry（status_id → AuraKey + AuraOffset，
    // 想给新状态配光环只在注册表加一行；真实特效放 Resources/ClientBattle/VFX/<key>，
    // 缺资源自动回退占位色块）。
    // 资源：多数光环仍用 Resources/ClientBattle/VFX prefab；
    // 三类挂法按 key 前缀分流（AuraKeyOf 给什么就走什么，注册表是唯一开关）：
    //   `shroud_`  罩身：包住一张卡，Fitter 定径 + Follower 跟随 + Presence 显隐；
    //   `ambient_` 场域氛围：**不挂卡**，钉主战场地面中心、按 key 全场去重
    //              （多人同状态只一份）、持有者清零才撤；几何见 StagePerformanceConfig；
    //   其余       普通光环：单实例挂卡心。
    // 宙斯：雷霆神谕＝ambient_thunder_storm（Magic Effect19 电弧场域）；落雷见 RemoteStrike。
    // 圣盾：All In 1 金色描边+辉光（UnitView.SetAegisAura）；
    // 阿瑞斯：血战＝卡框红呼吸；战神之勇＝shroud_* + VfxShroudPresence（显隐策略在注册表）。
    // 绕身显隐：VfxShroudPresence（IsPresent 闸受击抖动）；时机＝ShroudVisibility / SetShroudVisible。
    // 石化：UnitView.SetPetrified → All In 1 灰阶石色
    // 哈迪斯黑雾：强制极低透明度，避免整卡被黑住。
    // =========================================================================

    public static class UnitAuraService
    {
        // 冥雾必须极透：原黑云不透明会整卡变黑
        const float UnderworldAlphaMul = 0.12f;
        const float UnderworldSizeMul = 0.75f;
        const string AuraRootPrefix = "AuraMount";
        const string AmbientRootPrefix = "AmbientField";

        // (unit, statusId) → 光环实例；一单位一状态最多一个
        // 场域氛围件在这里的 fx 记 null（实例是全场共享的，见 _ambient），
        // 只借这张表记「谁还持有」，撤下逻辑统一。
        static readonly Dictionary<(UnitView, string), (string key, GameObject fx)> _active = new();

        /// <summary>场域氛围件：key → (全场唯一实例, 持有者集合)。
        /// 三个人身上都有【雷霆】也只有一份雷暴；最后一个持有者消失才撤。</summary>
        static readonly Dictionary<string, (GameObject fx, HashSet<(UnitView, string)> holders)>
            _ambient = new();

        /// <summary>当前回合号（round_start 写入）；绕身 Round 策略挂载时立刻对拍。</summary>
        static int _currentRound;

        /// <summary>回合开始：按注册表 ShroudVisibility 对所有绕身 Presence 自动对拍。</summary>
        public static void OnRoundStart(int roundNo)
        {
            if (roundNo <= 0) return;
            _currentRound = roundNo;
            foreach (var pair in _active)
            {
                if (pair.Value.fx == null) continue;
                var mode = Names.StatusPresentationRegistry.ShroudVisibilityOf(pair.Key.Item2);
                if (mode is Names.ShroudVisibility.Manual) continue;
                bool? show = EvaluateRoundVisibility(mode, roundNo);
                if (show == null) continue;
                var presence = pair.Value.fx.GetComponent<VfxShroudPresence>();
                presence?.SetShown(show.Value);
            }
        }

        static bool? EvaluateRoundVisibility(Names.ShroudVisibility mode, int roundNo) =>
            mode switch
            {
                Names.ShroudVisibility.Always => true,
                Names.ShroudVisibility.OddRounds => (roundNo & 1) == 1,
                Names.ShroudVisibility.EvenRounds => (roundNo & 1) == 0,
                _ => null,
            };

        /// <summary>状态施加：有配置则挂常驻循环光环（去重）。</summary>
        public static void OnStatusApplied(UnitView unit, string statusId)
        {
            string key = Names.StatusPresentationRegistry.AuraKeyOf(statusId);
            if (unit == null || key == null) return;
            if (_active.ContainsKey((unit, statusId))) return;

            var offset = Names.StatusPresentationRegistry.AuraOffsetOf(statusId);
            GameObject fx;
            if (key.StartsWith("shroud_", System.StringComparison.Ordinal))
                fx = MountShroud(key, unit, statusId);
            else if (key.StartsWith("ambient_", System.StringComparison.Ordinal))
                fx = RetainAmbientField(key, unit, statusId); // 全场共享，本表记 null
            else if (key.StartsWith("aura_fire"))
                fx = MountAresRage(key, statusId, unit);
            else
                fx = MountSingle(key, unit.transform, offset);
            _active[(unit, statusId)] = (key, fx);
        }

        /// <summary>状态移除：撤下对应光环。</summary>
        public static void OnStatusRemoved(UnitView unit, string statusId)
        {
            if (unit == null || !_active.TryGetValue((unit, statusId), out var entry)) return;
            _active.Remove((unit, statusId));
            ReleaseAmbientField(entry.key, (unit, statusId));
            if (entry.fx != null) Object.Destroy(entry.fx);
        }

        /// <summary>单位阵亡：其身上全部光环撤下。</summary>
        public static void OnUnitDefeated(UnitView unit)
        {
            var toRemove = new List<(UnitView, string)>();
            foreach (var pair in _active)
                if (pair.Key.Item1 == unit) toRemove.Add(pair.Key);
            foreach (var key in toRemove)
            {
                var entry = _active[key];
                ReleaseAmbientField(entry.key, key);
                if (entry.fx != null) Object.Destroy(entry.fx);
                _active.Remove(key);
            }
        }

        /// <summary>整局重置/跳到结尾：清空全部常驻光环与场域氛围件。</summary>
        public static void ClearAll()
        {
            foreach (var entry in _active.Values)
                if (entry.fx != null) Object.Destroy(entry.fx);
            _active.Clear();
            foreach (var entry in _ambient.Values)
                if (entry.fx != null) Object.Destroy(entry.fx);
            _ambient.Clear();
            _currentRound = 0;
        }

        /// <summary>单位是否有<strong>视觉上仍在场</strong>的绕身罩。
        /// 渐隐收干净后为 false（受击恢复抖动）；挂载但已隐 ≠ 有罩。
        /// 无 <see cref="VfxShroudPresence"/> 的 shroud_ 视为常显。</summary>
        public static bool HasShroud(UnitView unit)
        {
            if (unit == null) return false;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || pair.Value.fx == null) continue;
                var key = pair.Value.key;
                if (string.IsNullOrEmpty(key)
                    || !key.StartsWith("shroud_", System.StringComparison.Ordinal))
                    continue;
                var presence = pair.Value.fx.GetComponent<VfxShroudPresence>();
                if (presence == null) return true; // 无 Presence＝常显
                if (presence.IsPresent) return true;
            }
            return false;
        }

        /// <summary>该单位身上是否有**锁受击位移**的罩身（视觉在场 + 注册表置了
        /// <c>shroudLocksHitMotion</c>）。默认没有：罩身在场也照常击退与颤动，
        /// 罩由 <see cref="VfxShroudFollower"/> 跟着走，不会被甩出去。</summary>
        public static bool HasHitMotionLock(UnitView unit)
        {
            if (unit == null) return false;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || pair.Value.fx == null) continue;
                var key = pair.Value.key;
                if (string.IsNullOrEmpty(key)
                    || !key.StartsWith("shroud_", System.StringComparison.Ordinal))
                    continue;
                if (!Names.StatusPresentationRegistry.ShroudLocksHitMotion(pair.Key.Item2)) continue;
                var presence = pair.Value.fx.GetComponent<VfxShroudPresence>();
                if (presence == null || presence.IsPresent) return true;
            }
            return false;
        }

        /// <summary>任意时机显隐某状态的绕身（覆盖 Round 策略直到下次自动对拍或再次调用）。</summary>
        public static void SetShroudVisible(UnitView unit, string statusId, bool shown,
                                            bool instant = false)
        {
            if (unit == null || string.IsNullOrEmpty(statusId)) return;
            if (!_active.TryGetValue((unit, statusId), out var entry) || entry.fx == null) return;
            var presence = entry.fx.GetComponent<VfxShroudPresence>();
            presence?.SetShown(shown, instant);
        }

        /// <summary>任意时机显隐该单位全部绕身。</summary>
        public static void SetAllShroudsVisible(UnitView unit, bool shown, bool instant = false)
        {
            if (unit == null) return;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || pair.Value.fx == null) continue;
                if (string.IsNullOrEmpty(pair.Value.key)
                    || !pair.Value.key.StartsWith("shroud_", System.StringComparison.Ordinal))
                    continue;
                pair.Value.fx.GetComponent<VfxShroudPresence>()?.SetShown(shown, instant);
            }
        }

        // ---------------------------------------------------------- 挂载

        /// <summary>普通光环：单实例挂卡心（雷霆/圣盾特殊处理；潮汐/冥雾/冰锢…）。</summary>
        static GameObject MountSingle(string key, Transform host, Vector3 offset)
        {
            if (key == "aura_thunder")
                return MountThunderAura(host);
            if (key == "aura_aegis")
                return MountAegisAura(host);
            var root = NewRoot(key, host, offset);
            float scale = key == "aura_freeze" ? 0.55f : 1f; // CFXR3 Ice Shield
            var cell = SpawnCell(key, root.transform, Vector3.zero, scale);
            if (cell == null) FallbackPlaceholder(key, root.transform);
            else if (key == "aura_underworld") SoftenUnderworldFog(cell);
            return root;
        }

        /// <summary>势能火（CFXR3 Fire）：挂卡上缘循环粒子；scale 由档位决定。</summary>
        public static GameObject MountMomentumFire(Transform host, float scale)
        {
            var root = NewRoot("momentum_fire", host, Names.StatusPresentation.FireHeadOffset);
            var cell = SpawnCell("momentum_fire", root.transform, Vector3.zero, scale);
            if (cell == null) FallbackPlaceholder("momentum_fire", root.transform);
            return root;
        }

        /// <summary>势能卡后柔光：LightGlow A（已去星点）挂卡后 sorting −1；
        /// 关点光、轻柔化，边缘余晖随分档轻抬。</summary>
        public static GameObject MountMomentumGlow(Transform host, float scale)
        {
            var root = NewRoot("momentum_glow", host, new Vector3(0f, 0.04f, 0.5f));
            var cell = SpawnCell("momentum_glow", root.transform, Vector3.zero, scale);
            if (cell == null)
            {
                FallbackPlaceholder("momentum_glow", root.transform);
                SoftenMomentumAura(root);
            }
            else
                SoftenMomentumAura(cell);
            return root;
        }

        // 香槟金：偏暖白；轻拉不盖成荧光
        static readonly Color Champagne = new(1f, 0.94f, 0.78f, 1f);

        /// <summary>精致化：关 Point Light、剔星点、卡后层、轻柔粒子。</summary>
        static void SoftenMomentumAura(GameObject fx)
        {
            // 兜底：源 prefab 若又带 Star/Spark，运行时再剥掉
            var strip = new System.Collections.Generic.List<GameObject>();
            foreach (var t in fx.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == fx) continue;
                string n = t.name;
                if (n.IndexOf("Star", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Spark", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    strip.Add(t.gameObject);
            }
            foreach (var go in strip) Object.Destroy(go);

            foreach (var light in fx.GetComponentsInChildren<Light>(true))
                light.enabled = false;

            foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = -1;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startSizeMultiplier *= 0.95f;
                var emission = ps.emission;
                if (emission.enabled)
                    emission.rateOverTimeMultiplier *= 0.85f;

                var start = main.startColor;
                if (start.mode == ParticleSystemGradientMode.Color)
                {
                    var c = start.color;
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(
                        Mathf.Lerp(c.r, Champagne.r, 0.35f),
                        Mathf.Lerp(c.g, Champagne.g, 0.35f),
                        Mathf.Lerp(c.b, Champagne.b, 0.35f),
                        Mathf.Clamp01(c.a * 0.9f)));
                }
                else if (start.mode == ParticleSystemGradientMode.TwoColors)
                {
                    Color Soft(Color c) => new(
                        Mathf.Lerp(c.r, Champagne.r, 0.3f),
                        Mathf.Lerp(c.g, Champagne.g, 0.3f),
                        Mathf.Lerp(c.b, Champagne.b, 0.3f),
                        Mathf.Clamp01(c.a * 0.9f));
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        Soft(start.colorMin), Soft(start.colorMax));
                }
            }
        }

        /// <summary>圣盾：All In 1 金描边+辉光（挂在 UnitView 材质上；不挂 Magic 粒子）。</summary>
        static GameObject MountAegisAura(Transform host)
        {
            var unit = host.GetComponent<UnitView>() ?? host.GetComponentInParent<UnitView>();
            unit?.SetAegisAura(true);
            var root = NewRoot("aura_aegis", host, Vector3.zero);
            var marker = root.AddComponent<AegisAuraMarker>();
            marker.Unit = unit;
            return root;
        }

        /// <summary>通用绕身挂载：Fit+Follow + <see cref="VfxShroudPresence"/>。
        /// 初始显隐按注册表 ShroudVisibility 与当前回合对拍。</summary>
        static GameObject MountShroud(string key, UnitView unit, string statusId)
        {
            var root = NewRoot(key, unit != null ? unit.transform : null, Vector3.zero);
            root.transform.localRotation = Quaternion.identity;

            var prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
            if (prefab == null)
            {
                FallbackPlaceholder(key, root.transform);
                return root;
            }

            var cell = Object.Instantiate(prefab, root.transform);
            cell.name = key;
            cell.transform.localPosition = Vector3.zero;
            cell.transform.localRotation = Quaternion.identity;
            cell.transform.localScale = prefab.transform.localScale;

            DisableAutoLifecycle(cell);

            var follower = VfxShroudFollower.FitAndFollow(unit, cell, root.transform);
            // 三个人同时挂同一件时，三份实例逐帧同步地闪＝"一个动画复制了三份"
            VfxPhaseDesync.Apply(cell, StagePerformanceConfig.ShroudDesyncSeconds,
                                 StagePerformanceConfig.ShroudSpeedJitter);
            var presence = root.AddComponent<VfxShroudPresence>();
            presence.Bind(follower);

            var mode = Names.StatusPresentationRegistry.ShroudVisibilityOf(statusId);
            bool showNow = mode switch
            {
                Names.ShroudVisibility.Always => true,
                Names.ShroudVisibility.OddRounds => _currentRound > 0 && (_currentRound & 1) == 1,
                Names.ShroudVisibility.EvenRounds => _currentRound > 0 && (_currentRound & 1) == 0,
                _ => false, // Manual
            };
            presence.SetShown(showNow, instant: true);
            return root;
        }

        // ---------------------------------------------------- 场域氛围件

        /// <summary>持有一份场域氛围件（`ambient_*`）：**全场按 key 去重**，
        /// 首个持有者建实例、后续只登记引用。源点＝主战场地面中心
        /// （`ArenaSlotLayout.GroundCenter`，棋盘局部坐标），几何参数全在
        /// `StagePerformanceConfig.AmbientField*`。返回 null——实例不属于任何单位。</summary>
        static GameObject RetainAmbientField(string key, UnitView unit, string statusId)
        {
            if (_ambient.TryGetValue(key, out var entry) && entry.fx != null)
            {
                entry.holders.Add((unit, statusId));
                return null;
            }

            var board = unit != null ? unit.GetComponentInParent<BattleBoardView>() : null;
            var host = board != null ? board.BoardFxRoot : null;
            var root = new GameObject($"{AmbientRootPrefix}_{key}");
            root.transform.SetParent(host, false);
            root.transform.localPosition = ArenaSlotLayout.GroundCenter();
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
            if (prefab == null)
            {
                FallbackPlaceholder(key, root.transform);
            }
            else
            {
                var sources = StagePerformanceConfig.AmbientFieldSources;
                if (sources == null || sources.Length == 0)
                    sources = new[] { new StagePerformanceConfig.AmbientFieldSource
                                      { Name = "默认", Scale = 1f, Density = 1f, Lift = float.NaN } };
                foreach (var src in sources) MountAmbientSource(prefab, key, root.transform, src);
            }

            var holders = new HashSet<(UnitView, string)> { (unit, statusId) };
            _ambient[key] = (root, holders);
            return null;
        }

        /// <summary>挂一处场域源：位置按战场尺度换算（不写死世界数），
        /// 尺度/疏密/游走各自独立，几何全在 <see cref="StagePerformanceConfig"/>。</summary>
        static void MountAmbientSource(GameObject prefab, string key, Transform parent,
                                       StagePerformanceConfig.AmbientFieldSource src)
        {
            var pivot = new GameObject($"src_{(string.IsNullOrEmpty(src.Name) ? "?" : src.Name)}");
            pivot.transform.SetParent(parent, false);
            // 偏移按战场尺寸折算：换分辨率/换布局时源仍落在"中心区""天边"这两个语义位置上
            float lift = float.IsNaN(src.Lift) ? StagePerformanceConfig.AmbientFieldLift : src.Lift;
            pivot.transform.localPosition = new Vector3(
                src.X * BattlefieldLayout.MainHalfWidth,
                lift,
                src.Z * BattlefieldLayout.MainDepth * 0.5f);
            pivot.transform.localRotation = Quaternion.Euler(0f, src.Yaw, 0f);

            if (src.WanderRadius > 0f && src.WanderInterval > 0f)
            {
                var wander = pivot.AddComponent<AmbientFieldWander>();
                wander.Radius = src.WanderRadius * BattlefieldLayout.MainHalfWidth;
                wander.Interval = src.WanderInterval;
            }

            var cell = Object.Instantiate(prefab, pivot.transform);
            cell.name = key;
            cell.transform.localPosition = Vector3.zero;
            cell.transform.localRotation = Quaternion.identity;
            cell.transform.localScale = prefab.transform.localScale
                                        * StagePerformanceConfig.AmbientFieldScale
                                        * Mathf.Max(0.01f, src.Scale);
            DisableAutoLifecycle(cell);
            ForceLoop(cell);
            // 氛围压在卡牌之下：满屏元素盖在卡面前会把立绘/兵力糊掉
            foreach (var r in cell.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = StagePerformanceConfig.AmbientFieldSortingOrder;

            HideAmbientLayers(cell, src.HideLayers);
            ApplyAmbientDensity(cell, src.Density * StagePerformanceConfig.AmbientFieldDensity);
        }

        /// <summary>关掉本源里语义不成立的层（按节点名前缀）。只停用不销毁：
        /// 同一件的另一处源还要用这些层，销毁是对**实例**动手、停用才是对这一处动手。</summary>
        static void HideAmbientLayers(GameObject cell, string[] prefixes)
        {
            if (cell == null || prefixes == null || prefixes.Length == 0) return;
            foreach (var t in cell.GetComponentsInChildren<Transform>(true))
                foreach (var prefix in prefixes)
                    if (!string.IsNullOrEmpty(prefix)
                        && t.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        t.gameObject.SetActive(false);
                        break;
                    }
        }

        /// <summary>按倍数缩放这份实例的粒子发射量（rate 与 burst）。
        ///
        /// **必须在实例化之后做**：`VfxTierScale` 在 `OnEnable`（即 Instantiate 当场）
        /// 已按画质档从**原始值**写过一遍，我们在其后再乘，两者相乘即"档位 × 演出密度"，
        /// 各管各的。反过来若写在档位之前，会被档位那一步按原始值覆盖掉。
        /// 场域件不走池（每次挂载都是新实例），所以不存在重复相乘。</summary>
        static void ApplyAmbientDensity(GameObject cell, float density)
        {
            if (cell == null || Mathf.Approximately(density, 1f) || density <= 0f) return;
            foreach (var ps in cell.GetComponentsInChildren<ParticleSystem>(true))
            {
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= density;
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var burst = emission.GetBurst(i);
                    var count = burst.count;
                    // 至少留 1 颗：压到 0 等于把这层删了（只降强度，不删效果）
                    count.constantMin = Mathf.Max(1f, count.constantMin * density);
                    count.constantMax = Mathf.Max(1f, count.constantMax * density);
                    burst.count = count;
                    emission.SetBurst(i, burst);
                }
            }
        }

        /// <summary>释放一个持有者；持有者清零才真正撤下场域件。</summary>
        static void ReleaseAmbientField(string key, (UnitView, string) holder)
        {
            if (string.IsNullOrEmpty(key)
                || !key.StartsWith("ambient_", System.StringComparison.Ordinal)) return;
            if (!_ambient.TryGetValue(key, out var entry)) return;
            entry.holders.Remove(holder);
            if (entry.holders.Count > 0) return;
            if (entry.fx != null) Object.Destroy(entry.fx);
            _ambient.Remove(key);
        }

        /// <summary>圣盾挂载标记：销毁时关掉材质效果。</summary>
        class AegisAuraMarker : MonoBehaviour
        {
            public UnitView Unit;
            void OnDestroy()
            {
                if (Unit != null) Unit.SetAegisAura(false);
            }
        }

        /// <summary>宙斯雷霆常驻：Digital Ruby 闪电 + 卡面乱劈调度。</summary>
        static GameObject MountThunderAura(Transform host)
        {
            var root = NewRoot("aura_thunder", host, new Vector3(0f, 0f, -0.5f));
            root.AddComponent<ThunderAuraDriver>();
            return root;
        }

        /// <summary>阿瑞斯血战：卡框红色呼吸（战神之勇已改挂 Magic 常驻环）。</summary>
        static GameObject MountAresRage(string key, string statusId, UnitView unit)
        {
            float strength = 0.55f;
            float existing = 0f;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || !pair.Value.key.StartsWith("aura_fire")) continue;
                existing = Mathf.Max(existing, 0.55f);
            }
            unit?.SetAresRage(true, Mathf.Max(existing, strength));

            var root = NewRoot(key, unit != null ? unit.transform : null, Vector3.zero);
            var marker = root.AddComponent<AresRageMarker>();
            marker.Unit = unit;
            return root;
        }

        /// <summary>怒火挂载移除后：若还有血战则保持呼吸，否则关闭。</summary>
        static void RefreshAresRage(UnitView unit)
        {
            if (unit == null) return;
            float strength = 0f;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || !pair.Value.key.StartsWith("aura_fire")) continue;
                strength = Mathf.Max(strength, 0.55f);
            }
            unit.SetAresRage(strength > 0f, strength);
        }

        class AresRageMarker : MonoBehaviour
        {
            public UnitView Unit;
            void OnDestroy()
            {
                // 从 _active 移除发生在 Destroy 之前；此处扫描剩余条目
                RefreshAresRage(Unit);
            }
        }

        // ---------------------------------------------------------- 通用

        static void SoftenUnderworldFog(GameObject fx)
        {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startSizeMultiplier *= UnderworldSizeMul;

                var c = main.startColor.color;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(c.r, c.g, c.b, Mathf.Clamp01(c.a) * UnderworldAlphaMul));

                var col = ps.colorOverLifetime;
                if (col.enabled)
                {
                    col.color = new ParticleSystem.MinMaxGradient(
                        new Color(0.15f, 0.05f, 0.2f, 0.08f),
                        new Color(0.05f, 0.02f, 0.1f, 0.02f));
                }

                var emission = ps.emission;
                if (emission.rateOverTime.constant > 4f)
                    emission.rateOverTime = new ParticleSystem.MinMaxCurve(3f);

                ps.Clear(true);
                ps.Play(true);
            }

            foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r.sharedMaterial == null) continue;
                var mat = r.material;
                if (mat.HasProperty("_Color"))
                {
                    var c = mat.GetColor("_Color");
                    mat.SetColor("_Color", new Color(c.r, c.g, c.b, c.a * UnderworldAlphaMul));
                }
                if (mat.HasProperty("_BaseColor"))
                {
                    var c = mat.GetColor("_BaseColor");
                    mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, c.a * UnderworldAlphaMul));
                }
                if (mat.HasProperty("_TintColor"))
                {
                    var c = mat.GetColor("_TintColor");
                    mat.SetColor("_TintColor", new Color(c.r, c.g, c.b, c.a * UnderworldAlphaMul));
                }
            }
        }

        static GameObject NewRoot(string key, Transform host, Vector3 offset)
        {
            var root = new GameObject($"{AuraRootPrefix}_{key}");
            root.transform.SetParent(host, false);
            root.transform.localPosition = offset;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            return root;
        }

        /// <summary>实例化一份特效：禁自动销毁、强制循环、渲染层压到 15（卡牌之上、图标之下）。</summary>
        static GameObject SpawnCell(string key, Transform parent, Vector3 localPos, float scale)
        {
            var prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
            if (prefab == null) return null;
            var cell = Object.Instantiate(prefab, parent);
            cell.transform.localPosition = localPos;
            cell.transform.localRotation = Quaternion.identity;
            cell.transform.localScale = prefab.transform.localScale * scale;

            DisableAutoLifecycle(cell);
            ForceLoop(cell);
            foreach (var r in cell.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = 15;
            return cell;
        }

        /// <summary>CFXR_Effect / 厂包定时自毁等会在播完时销毁/停用实例，常驻挂载必须禁掉。</summary>
        static void DisableAutoLifecycle(GameObject fx)
        {
            foreach (var mb in fx.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue; // missing script 容错
                var typeName = mb.GetType().Name;
                if (typeName == "CFXR_Effect" || typeName == "VfxAutoDestruct"
                    || typeName.Contains("DeactivateByTime"))
                    mb.enabled = false;
            }
        }

        /// <summary>一次性特效强制循环（循环特效本身不受影响）。</summary>
        static void ForceLoop(GameObject fx)
        {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                ps.Play();
            }
        }

        /// <summary>缺资源占位（沿用 VFXManager 占位色块，直接挂 root 下）。</summary>
        static void FallbackPlaceholder(string key, Transform parent)
        {
            var cell = VFXManager.Ensure().PlayOn(key, parent, duration: -1f);
            foreach (var r in cell.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = 15;
        }
    }
}
