using ClientBattle.Events;
using ClientBattle.Names;
using ClientBattle.Units;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】CutInPolicy：全部 cut-in 触发判定与阈值（R-5.2）。
    //
    // 触发源四类：满档（服务 cut_in + 镜像已满轨）、巨伤（>阈值）、
    // 行动窗第 5 次追击、战术变更（EventApplyService 横幅回退）。
    // 去重（同 group 只播一次）在 CutInService 内部。
    // 改 cut-in 规则只改本文件，禁止散落编排/演出层。
    //
    // 【统一编排】前三类都有主体武将 → 一律走 <see cref="CutInStage"/> 的
    // 「推镜 → 横幅 → 本组出手命中 → 撤镜」独占单元（与单挑同构，不飞立绘）。
    // 判定必须在**播组之前**（<see cref="Resolve"/>）：客户端播一组前就持有整组
    // 事件，能提前知道这一下会不会打出巨伤；事后回调式做不到「伤害前推镜」，
    // 还会让暗幕盖住刚起播的命中特效（P-72）。
    // =========================================================================

    public static class CutInPolicy
    {
        /// <summary>巨伤 cut-in 门槛：单条 damage 超过即触发「重创」取景 cut-in。</summary>
        public const float HighDamageThreshold = 3000f;

        /// <summary>行动窗内第 N 次追击单元触发「追击不止」cut-in。</summary>
        public const int PursuitCutInAt = 5;

        public static bool IsHighDamage(DamageEvent damage) =>
            damage != null && damage.Amount > HighDamageThreshold;

        /// <summary>一次取景 cut-in 的判定结果。<c>Kind</c> 只用于是否给
        /// 加强出手标记（满档才给）。</summary>
        public readonly struct Decision
        {
            public readonly string HeroId;
            public readonly string Title;
            public readonly int GroupId;
            public readonly bool Empowered;

            public Decision(string heroId, string title, int groupId, bool empowered)
            {
                HeroId = heroId; Title = title; GroupId = groupId; Empowered = empowered;
            }
        }

        /// <summary>播组**之前**决定这一组要不要切 cut-in（唯一入口）。
        /// 优先级：满档 &gt; 巨伤 &gt; 追击第 N 次；一组最多一次。
        /// <paramref name="pursuitCount"/> 为本行动窗内已数到的追击单元序号。</summary>
        public static Decision? Resolve(EventGroup group, int pursuitCount)
        {
            var full = FindFullTrackCutIn(group);
            if (full != null)
                return new Decision(full.HeroId, $"{SkillNameOf(group)}！", full.GroupId,
                                    empowered: true);

            var huge = FindHighDamage(group);
            if (huge != null)
                return new Decision(huge.SourceId,
                    $"{SkillNameOf(group)} 重创 {huge.TargetId}！-{huge.Amount}",
                    huge.GroupId, empowered: false);

            if (group.Kind == GroupKind.Pursuit && pursuitCount == PursuitCutInAt)
            {
                string pursuer = group.Root is SkillTriggerEvent pst ? pst.ActorId : null;
                if (!string.IsNullOrEmpty(pursuer))
                    return new Decision(pursuer, "追击不止！", group.Root.GroupId,
                                        empowered: false);
            }
            return null;
        }

        /// <summary>组内第一条巨额伤害（>阈值且真实掉血）。被格挡/反弹的 0 伤不算：
        /// 那一下没打进去，切「重创」横幅是假的。</summary>
        public static DamageEvent FindHighDamage(EventGroup group)
        {
            foreach (var ev in group.Events)
            {
                if (ev is not DamageEvent d) continue;
                if (!string.IsNullOrEmpty(d.Mitigation)) continue;
                if (IsHighDamage(d)) return d;
            }
            return null;
        }

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
