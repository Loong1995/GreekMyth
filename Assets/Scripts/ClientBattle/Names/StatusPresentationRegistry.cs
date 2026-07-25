using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.Names
{
    // =========================================================================
    // 状态表现注册表（2026-07-20 重构收口）：一个状态在客户端的全部表现配置
    // 集中在这一张表——新增状态只在这里加一行，各服务只读本表：
    //   AuraKey      → UnitAuraService 常驻光环（null = 无光环）
    //   AuraOffset   → 挂载相对卡牌中心的偏移（底火/顶火等；默认卡心微上）
    //   ControlIcon  → StatusIconPanel 卡顶外侧横排图标（硬控 + 冥火等；
    //                    先攻/犹豫不展示图标，仅飘字/台词）
    //   StatsSkillId → BattleSkillStatsAggregator 结算表归因的战法格子
    //                  （null = 直通 status_id；神谕/被动载体 status_id ≠ skill_id）
    //   CollectiveMerge → CollectiveTriggerMergeProcessor 相邻同状态同来源触发
    //                     合并为一次集体齐发（雷霆）
    // 与后端对应：battle/statuses.py + skills_*.py 的 StatusDef；中文名见 ChineseNames。
    // =========================================================================

    public readonly struct StatusPresentation
    {
        public static readonly Vector3 DefaultAuraOffset = new(0f, 0.1f, -0.5f);
        /// <summary>卡牌底部持续火焰（怒火类）。</summary>
        public static readonly Vector3 FireFootOffset = new(0f, -1.05f, -0.5f);
        /// <summary>卡牌头顶持续火焰（战神之勇）。</summary>
        public static readonly Vector3 FireHeadOffset = new(0f, 1.3f, -0.5f);

        public readonly string AuraKey;
        public readonly Vector3 AuraOffset;
        public readonly bool ControlIcon;
        public readonly string StatsSkillId;
        public readonly bool CollectiveMerge;

        public StatusPresentation(string auraKey = null, bool controlIcon = false,
                                  string statsSkillId = null, bool collectiveMerge = false,
                                  Vector3? auraOffset = null)
        {
            AuraKey = auraKey;
            AuraOffset = auraOffset ?? DefaultAuraOffset;
            ControlIcon = controlIcon;
            StatsSkillId = statsSkillId;
            CollectiveMerge = collectiveMerge;
        }
    }

    public static class StatusPresentationRegistry
    {
        static readonly Dictionary<string, StatusPresentation> Table = new()
        {
            // ---- 卡顶外侧图标（ControlIcon；先攻/犹豫刻意不列入，仅飘字+状态台词）----
            ["silence"] = new(controlIcon: true),
            ["disarm"] = new(controlIcon: true),
            ["petrify"] = new(controlIcon: true),
            ["freeze"] = new(controlIcon: true, auraKey: "aura_freeze",
                             auraOffset: new Vector3(0f, -0.3f, -0.5f)),
            ["ming_lock"] = new(controlIcon: true),
            ["charm"] = new(controlIcon: true),
            ["fear"] = new(controlIcon: true),

            // ---- 神谕 / 被动载体（光环 + 结算归因）----
            ["thunder"] = new(auraKey: "aura_thunder", statsSkillId: "thunder_oracle",
                              collectiveMerge: true),
            ["aegis_shield"] = new(auraKey: "aura_aegis", statsSkillId: "athena_aegis"),
            ["aegis_ward"] = new(statsSkillId: "athena_aegis"),
            ["snake_staff_protection"] = new(statsSkillId: "asclepius_oracle"),
            ["snake_staff_tender"] = new(statsSkillId: "asclepius_oracle"),
            // 阿瑞斯：血战＝卡框红呼吸；战神之勇＝Magic Effect18 常驻（无呼吸）
            ["blood_battle"] = new(auraKey: "aura_fire_foot", statsSkillId: "ares_warfury",
                                   auraOffset: StatusPresentation.FireFootOffset),
            ["ares_might"] = new(auraKey: "aura_ares_might", statsSkillId: "ares_warfury"),
            ["war_frenzy"] = new(statsSkillId: "ares_frenzy"),
            ["divine_revelation"] = new(auraKey: "aura_sunlight", statsSkillId: "delphi_revelation"),
            ["nike_wings"] = new(auraKey: "aura_sunlight"),
            ["patroclus_standin"] = new(statsSkillId: "patroclus_standin"),
            ["hermes_herald_mark"] = new(auraKey: "aura_hermes_mark", statsSkillId: "hermes_oracle"),
            ["hermes_confusion_mark"] = new(auraKey: "aura_hermes_mark", statsSkillId: "hermes_oracle"),
            ["poseidon_tide"] = new(auraKey: "aura_tide", statsSkillId: "poseidon_oracle"),
            ["hades_lifesteal"] = new(auraKey: "aura_underworld", statsSkillId: "hades_underworld_dominion"),
            ["shadow_veil"] = new(auraKey: "aura_underworld", statsSkillId: "hades_underworld_dominion"),
            ["hades_command_drain"] = new(auraKey: "aura_underworld", statsSkillId: "hades_underworld_dominion"),
            // 冥火：与硬控同槽卡顶图标；无光环、不用 CFXR 火
            ["underworld_burn"] = new(controlIcon: true, statsSkillId: "hecate_torch"),
            ["hecate_torch"] = new(statsSkillId: "hecate_torch"),
            ["lion_counter"] = new(statsSkillId: "heracles_counter"),
            ["trojan_bomb"] = new(statsSkillId: "odysseus_trojan"),
            ["trojan_scheme"] = new(statsSkillId: "odysseus_trojan"),
            ["perseus_mirror"] = new(statsSkillId: "perseus_relics"),
            ["achilles_thrust_crit"] = new(statsSkillId: "achilles_thrust"),
        };

        public static bool IsControl(string statusId)
            => !string.IsNullOrEmpty(statusId)
               && Table.TryGetValue(statusId, out var p) && p.ControlIcon;

        /// <summary>常驻光环 key；无配置返回 null。</summary>
        public static string AuraKeyOf(string statusId)
            => !string.IsNullOrEmpty(statusId) && Table.TryGetValue(statusId, out var p)
               ? p.AuraKey : null;

        /// <summary>常驻光环相对卡牌偏移；无配置返回默认卡心微上。</summary>
        public static Vector3 AuraOffsetOf(string statusId)
            => !string.IsNullOrEmpty(statusId) && Table.TryGetValue(statusId, out var p)
               ? p.AuraOffset : StatusPresentation.DefaultAuraOffset;

        /// <summary>结算表归因战法 id；未登记直通 status_id（skill 同名状态零配置）。</summary>
        public static string StatsSkillOf(string statusId)
        {
            if (string.IsNullOrEmpty(statusId)) return "unknown";
            if (Table.TryGetValue(statusId, out var p) && !string.IsNullOrEmpty(p.StatsSkillId))
                return p.StatsSkillId;
            return statusId;
        }

        public static bool IsCollective(string statusId)
            => !string.IsNullOrEmpty(statusId)
               && Table.TryGetValue(statusId, out var p) && p.CollectiveMerge;
    }
}
