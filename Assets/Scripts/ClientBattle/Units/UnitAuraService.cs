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
    // 宙斯：Digital Ruby LightningBolt（DrLightningUtil）+ ThunderAuraDriver 调度；
    // 圣盾：All In 1 金色描边+辉光（UnitView.SetAegisAura）；
    // 阿瑞斯：卡框红色呼吸（血战弱 / 战神之勇强）；不再用 FireRimFx。
    // 宙斯雷霆：卡面频繁落劈；触发贯穿见 RemoteStrike
    // 石化：UnitView.SetPetrified → All In 1 灰阶石色
    // 哈迪斯黑雾：强制极低透明度，避免整卡被黑住。
    // =========================================================================

    public static class UnitAuraService
    {
        // 冥雾必须极透：原黑云不透明会整卡变黑
        const float UnderworldAlphaMul = 0.12f;
        const float UnderworldSizeMul = 0.75f;
        const string AuraRootPrefix = "AuraMount";

        // (unit, statusId) → 光环实例；一单位一状态最多一个
        static readonly Dictionary<(UnitView, string), (string key, GameObject fx)> _active = new();

        /// <summary>状态施加：有配置则挂常驻循环光环（去重）。</summary>
        public static void OnStatusApplied(UnitView unit, string statusId)
        {
            string key = Names.StatusPresentationRegistry.AuraKeyOf(statusId);
            if (unit == null || key == null) return;
            if (_active.ContainsKey((unit, statusId))) return;

            var offset = Names.StatusPresentationRegistry.AuraOffsetOf(statusId);
            GameObject fx = key.StartsWith("aura_fire")
                ? MountAresRage(key, statusId, unit)
                : MountSingle(key, unit.transform, offset);
            _active[(unit, statusId)] = (key, fx);
        }

        /// <summary>状态移除：撤下对应光环。</summary>
        public static void OnStatusRemoved(UnitView unit, string statusId)
        {
            if (unit == null || !_active.TryGetValue((unit, statusId), out var entry)) return;
            _active.Remove((unit, statusId));
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
                if (_active[key].fx != null) Object.Destroy(_active[key].fx);
                _active.Remove(key);
            }
        }

        /// <summary>整局重置/跳到结尾：清空全部常驻光环。</summary>
        public static void ClearAll()
        {
            foreach (var entry in _active.Values)
                if (entry.fx != null) Object.Destroy(entry.fx);
            _active.Clear();
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

        /// <summary>圣盾：All In 1 金描边+辉光（挂在 UnitView 材质上）。</summary>
        static GameObject MountAegisAura(Transform host)
        {
            var unit = host.GetComponent<UnitView>() ?? host.GetComponentInParent<UnitView>();
            unit?.SetAegisAura(true);
            var root = NewRoot("aura_aegis", host, Vector3.zero);
            var marker = root.AddComponent<AegisAuraMarker>();
            marker.Unit = unit;
            return root;
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

        /// <summary>阿瑞斯怒火：卡框红色呼吸（替代火舌）。</summary>
        static GameObject MountAresRage(string key, string statusId, UnitView unit)
        {
            float strength = statusId == "ares_might" ? 1f : 0.55f;
            // 若已有更强怒火，取 max
            float existing = 0f;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || !pair.Value.key.StartsWith("aura_fire")) continue;
                existing = Mathf.Max(existing, pair.Key.Item2 == "ares_might" ? 1f : 0.55f);
            }
            unit?.SetAresRage(true, Mathf.Max(existing, strength));

            var root = NewRoot(key, unit != null ? unit.transform : null, Vector3.zero);
            var marker = root.AddComponent<AresRageMarker>();
            marker.Unit = unit;
            return root;
        }

        /// <summary>怒火挂载移除后：若还有其他火系状态则保持较弱/强档，否则关闭。</summary>
        static void RefreshAresRage(UnitView unit)
        {
            if (unit == null) return;
            float strength = 0f;
            foreach (var pair in _active)
            {
                if (pair.Key.Item1 != unit || !pair.Value.key.StartsWith("aura_fire")) continue;
                strength = Mathf.Max(strength, pair.Key.Item2 == "ares_might" ? 1f : 0.55f);
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

        /// <summary>CFXR_Effect 等特效包脚本会在播完时销毁/停用实例，常驻挂载必须禁掉。</summary>
        static void DisableAutoLifecycle(GameObject fx)
        {
            foreach (var mb in fx.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue; // missing script 容错
                var typeName = mb.GetType().Name;
                if (typeName == "CFXR_Effect" || typeName == "VfxAutoDestruct")
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
