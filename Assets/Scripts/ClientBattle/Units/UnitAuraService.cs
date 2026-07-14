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
    // 数据源 StatusAuraTable：status_id → 光环 VFX key（想给新状态配光环只加一行；
    // 真实特效放 Resources/ClientBattle/VFX/<key>，缺资源自动回退占位色块）。
    // 关键处理：已购包的光环 prefab 多为一次性 flipbook（播完粒子归零就"看不见"），
    // 挂载时强制全部 ParticleSystem 循环播放，保证常驻可见。
    // =========================================================================

    public static class UnitAuraService
    {
        /// <summary>状态 → 常驻光环 key（client_perform §二 逐战法规格）。</summary>
        static readonly Dictionary<string, string> StatusAuraTable = new()
        {
            ["thunder"] = "aura_thunder",                 // 雷霆神谕：闪电缠绕
            ["aegis_shield"] = "aura_aegis",              // 埃癸斯圣盾：圣盾环绕
            ["blood_battle"] = "aura_bloodlust_weak",     // 战神怒火（全场）：弱血红
            ["ares_might"] = "aura_bloodlust_strong",     // 战神之勇（最强者）：强血红
            ["divine_revelation"] = "aura_sunlight",      // 德尔斐启示：呼吸阳光
            ["nike_wings"] = "aura_sunlight",             // 胜利羽翼：复用阿波罗阳光
            ["hermes_herald_mark"] = "aura_hermes_mark",  // 神使印记
            ["hermes_confusion_mark"] = "aura_hermes_mark", // 扰心印记（同资源）
        };

        // (unit, statusId) → 光环实例；一单位一状态最多一个
        static readonly Dictionary<(UnitView, string), (string key, GameObject fx)> _active = new();

        /// <summary>状态施加：有配置则挂常驻循环光环（去重）。</summary>
        public static void OnStatusApplied(UnitView unit, string statusId)
        {
            if (unit == null || !StatusAuraTable.TryGetValue(statusId, out var key)) return;
            if (_active.ContainsKey((unit, statusId))) return;

            // z=-0.5 在卡牌之前；粒子渲染层也要抬到卡牌元素（0~5）与状态图标（20/30）之间
            var fx = VFXManager.Ensure().PlayOn(key, unit.transform,
                duration: -1f, offset: new Vector3(0f, 0.1f, -0.5f));
            ForceLoop(fx);
            foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
                r.sortingOrder = 15;
            _active[(unit, statusId)] = (key, fx);
        }

        /// <summary>状态移除：撤下对应光环。</summary>
        public static void OnStatusRemoved(UnitView unit, string statusId)
        {
            if (unit == null || !_active.TryGetValue((unit, statusId), out var entry)) return;
            _active.Remove((unit, statusId));
            VFXManager.Instance?.Release(entry.key, entry.fx);
        }

        /// <summary>单位阵亡：其身上全部光环撤下。</summary>
        public static void OnUnitDefeated(UnitView unit)
        {
            var toRemove = new List<(UnitView, string)>();
            foreach (var pair in _active)
                if (pair.Key.Item1 == unit) toRemove.Add(pair.Key);
            foreach (var key in toRemove)
            {
                VFXManager.Instance?.Release(_active[key].key, _active[key].fx);
                _active.Remove(key);
            }
        }

        /// <summary>整局重置/跳到结尾：清空全部常驻光环。</summary>
        public static void ClearAll()
        {
            foreach (var entry in _active.Values)
                VFXManager.Instance?.Release(entry.key, entry.fx);
            _active.Clear();
        }

        /// <summary>一次性 flipbook 粒子强制循环 + 提升发射密度（常驻光环必须持续可见；
        /// 已购包的战斗特效多为单次爆发，直接常驻挂载几乎看不到）。</summary>
        static void ForceLoop(GameObject fx)
        {
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                var emission = ps.emission;
                emission.enabled = true;
                // 持续发射过稀（含 0，纯 burst 型）时补到 3/s，保证环绕感
                if (emission.rateOverTime.constant < 3f)
                    emission.rateOverTime = new ParticleSystem.MinMaxCurve(3f);
                // 常驻挂身特效压半透明：环绕氛围而非遮脸
                var color = main.startColor;
                var c = color.color;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(c.r, c.g, c.b, c.a * 0.55f));
                ps.Play();
            }
        }
    }
}
