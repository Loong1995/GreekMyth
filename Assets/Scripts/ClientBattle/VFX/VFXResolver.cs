using System.Collections.Generic;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第3层 特效解析】三级优先级查找：特殊配置 → 组默认 → 全默认。
    //
    // - 输入一个 EventGroup，输出"怎么演"（PerformanceProfile）+ 该用哪个模板。
    // - 完全没配则整体走默认库；任何情况都必须能播出东西。
    // - 未配置的 skillId 首次遇到时 Debug.LogWarning 提示（去重，不刷屏）。
    // =========================================================================

    public class VFXResolver
    {
        readonly PerformanceDatabase _db;
        readonly HashSet<string> _warned = new();

        public VFXResolver(PerformanceDatabase db)
        {
            _db = db != null ? db : PerformanceDatabase.BuildRuntimeDefault();
        }

        /// <summary>解析一个播放单元的演出配置。</summary>
        public PerformanceProfile Resolve(EventGroup group)
        {
            string id = KeyOf(group);

            // 1. 特殊配置（最高优先级）
            if (!string.IsNullOrEmpty(id))
            {
                var special = _db.FindSpecial(id);
                if (special != null) return special;
                WarnOnce(id, group.Kind);
            }

            // 2. 组默认
            var groupDefault = group.Kind switch
            {
                GroupKind.ActiveSkill => _db.ActiveDefault,
                GroupKind.NormalAttack => _db.MeleeDefault,
                GroupKind.Pursuit => _db.PursuitDefault,
                GroupKind.StatusTrigger => _db.StatusTriggerDefault,
                GroupKind.Passive => _db.OracleDefault,
                _ => null,
            };
            if (groupDefault != null) return groupDefault;

            // 3. 全默认（兜底，必能播）
            return _db.GlobalDefault ?? new PerformanceProfile();
        }

        /// <summary>组的配置匹配键：状态触发用状态 id，其余用战法 id。</summary>
        public static string KeyOf(EventGroup group)
        {
            switch (group.Root)
            {
                case SkillTriggerEvent st: return st.SkillId;
                case StatusTickEvent tick: return tick.Status?.StatusId;
                case NormalAttackEvent: return "basic_attack";
                case StatusApplyEvent apply: return apply.Status?.StatusId;
                case StatusRemoveEvent remove: return remove.Status?.StatusId;
                default: return null;
            }
        }

        void WarnOnce(string id, GroupKind kind)
        {
            if (id == "basic_attack" || _warned.Contains(id)) return;
            _warned.Add(id);
            Debug.LogWarning($"[ClientBattle] skillId/statusId '{id}'（{kind}）无特殊演出配置，走组默认策略");
        }
    }
}
