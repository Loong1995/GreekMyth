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
    //   ShroudVisibility → 绕身（shroud_*）显隐策略；驱动 VfxShroudPresence
    // 与后端对应：battle/statuses.py + skills_*.py 的 StatusDef；中文名见 ChineseNames。
    // =========================================================================

    /// <summary>绕身显隐时机（挂在 StatusPresentation；驱动 <see cref="VFX.VfxShroudPresence"/>）。
    /// 可随时用 <c>UnitAuraService.SetShroudVisible</c> 覆盖；Round 策略仅在 round_start 自动对拍。</summary>
    public enum ShroudVisibility
    {
        /// <summary>挂上即常显（无 Presence 渐隐，或 Presence 恒 Show）。</summary>
        Always = 0,
        /// <summary>奇数回合显、偶数回合隐（战神之勇）。</summary>
        OddRounds = 1,
        /// <summary>偶数回合显、奇数回合隐。</summary>
        EvenRounds = 2,
        /// <summary>只听手动 SetShroudVisible；挂载默认隐。</summary>
        Manual = 3,
    }

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
        /// <summary>绕身（AuraKey 以 shroud_ 开头）的显隐策略；非绕身忽略。</summary>
        public readonly ShroudVisibility ShroudVisibility;

        public StatusPresentation(string auraKey = null, bool controlIcon = false,
                                  string statsSkillId = null, bool collectiveMerge = false,
                                  Vector3? auraOffset = null,
                                  ShroudVisibility shroudVisibility = ShroudVisibility.Always)
        {
            AuraKey = auraKey;
            AuraOffset = auraOffset ?? DefaultAuraOffset;
            ControlIcon = controlIcon;
            StatsSkillId = statsSkillId;
            CollectiveMerge = collectiveMerge;
            ShroudVisibility = shroudVisibility;
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
            // 雷霆：Magic Effect19 **场域氛围件**（画廊 2/8·11/61）——`ambient_` 前缀＝
            // 不挂卡、钉主战场地面中心铺满全场，全场按 key 去重（多人有【雷霆】只一份雷暴）。
            // 落雷触发仍走 RemoteStrike。
            ["thunder"] = new(auraKey: "ambient_thunder_storm", statsSkillId: "thunder_oracle",
                              collectiveMerge: true),
            ["aegis_shield"] = new(auraKey: "aura_aegis", statsSkillId: "athena_aegis"),
            ["aegis_ward"] = new(statsSkillId: "athena_aegis"),
            ["snake_staff_protection"] = new(statsSkillId: "asclepius_oracle"),
            ["snake_staff_tender"] = new(statsSkillId: "asclepius_oracle"),
            // 阿瑞斯：血战＝卡框红呼吸；战神之勇＝Magic Effect18 罩身（奇数回合显）
            ["blood_battle"] = new(auraKey: "aura_fire_foot", statsSkillId: "ares_warfury",
                                   auraOffset: StatusPresentation.FireFootOffset),
            ["ares_might"] = new(auraKey: "shroud_ares_might", statsSkillId: "ares_warfury",
                                 shroudVisibility: ShroudVisibility.OddRounds),
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

        /// <summary>绕身显隐策略；未登记或非绕身默认 Always。</summary>
        public static ShroudVisibility ShroudVisibilityOf(string statusId)
            => !string.IsNullOrEmpty(statusId) && Table.TryGetValue(statusId, out var p)
               ? p.ShroudVisibility : ShroudVisibility.Always;
    }
}
