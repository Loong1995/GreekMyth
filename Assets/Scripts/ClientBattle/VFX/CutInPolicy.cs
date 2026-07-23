using ClientBattle.Events;
using ClientBattle.Names;
using ClientBattle.Units;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】CutInPolicy：全部 cut-in 触发判定与阈值（R-5.2）。
    //
    // 触发源四类：满档（服务 cut_in + 镜像已满轨）、高伤（>阈值）、
    // 行动窗第 5 次追击、战术变更（EventApplyService 横幅回退）。
    // 去重（同 group 只播一次）在 CutInService 内部。
    // 改 cut-in 规则只改本文件，禁止散落编排/演出层。
    // =========================================================================

    public static class CutInPolicy
    {
        /// <summary>高伤 cut-in 门槛：单条 damage 超过即请求非阻塞 cut-in。</summary>
        public const float HighDamageThreshold = 3000f;

        /// <summary>行动窗内第 N 次追击单元触发「追击不止」cut-in。</summary>
        public const int PursuitCutInAt = 5;

        public static bool IsHighDamage(DamageEvent damage) =>
            damage != null && damage.Amount > HighDamageThreshold;

        /// <summary>找出本组内「轨已满后再次进账」的满档 cut-in（出手前阻塞预播）。
        /// 落账前镜像 ≥ Full(5) 且事件 cut_in——刚满 5 的当次不切；其他轨互不影响。</summary>
        public static MomentumChangeEvent FindFullTrackCutIn(EventGroup group)
        {
            foreach (var ev in group.Events)
            {
                if (ev is not MomentumChangeEvent { CutIn: true } m) continue;
                if (!MomentumService.TrackTable.ContainsKey(m.Track)) continue;
                if (MomentumService.ValueOf(m.HeroId, m.Track) >= MomentumService.Full)
                    return m;
            }
            return null;
        }

        /// <summary>组的技能显示名（满档 cut-in 标题）：即将造成伤害的技能/普攻/状态归因战法。</summary>
        public static string SkillNameOf(EventGroup group)
        {
            switch (group.Root)
            {
                case SkillTriggerEvent st:
                    return ChineseNames.Skill(st.SkillId);
                case NormalAttackEvent { Kind: "coordinated" }:
                    return "协击";
                case NormalAttackEvent:
                    return "普攻";
                case StatusTickEvent tick:
                {
                    // 状态触发 → 归因到来源战法中文名（如 achilles_wrath → 阿喀琉斯之怒）
                    string statusId = tick.Status?.StatusId ?? "";
                    string skillId = StatusPresentationRegistry.StatsSkillOf(statusId);
                    string skillName = ChineseNames.Skill(skillId);
                    if (skillName != skillId) return skillName;
                    return ChineseNames.Status(statusId);
                }
                default:
                    // 兜底：组内若有 skill_trigger 副事件（少见）取其名
                    foreach (var ev in group.Events)
                        if (ev is SkillTriggerEvent st)
                            return ChineseNames.Skill(st.SkillId);
                    return "强袭";
            }
        }
    }
}
