using System;
using System.Collections.Generic;
using ClientBattle.Names;

namespace ClientBattle.Events
{
    // =========================================================================
    // 【第2层 事件管线·编译期 pass】CutInPlanner：全部取景 cut-in 的判定与阈值
    // （R-5.2，原 L4 CutInPolicy，2026-07-27 下沉为编译期注记）。
    //
    // 触发源：武将专属高光（服务 hint.cut_in=highlight 点名）、满档（服务 cut_in +
    // 势能预演已满轨）、巨伤「重创」（>阈值且真实掉血）、行动窗第 5 次追击。
    // 判定在**编译期**逐组注记到 EventGroup.CutIn，
    // 运行期 Director 只读注记，不再持有追击计数或查势能镜像——
    // 「先播了才知道要切」的回调式表现是 P-72 的结构性根因。
    //
    // 【势能预演】满档判据需要「落账前镜像值」。势能事件自带落账后 Value，
    // 预演即按组序重放 (hero,track)→Value，判定读**应用本组之前**的值，
    // 与运行期 MomentumService 镜像逐组等价（同一事件流、同一次序）。
    //
    // 战术变更 cut-in（无主体，文字横幅回退）不在本 pass：那是落账路径的
    // 非阻塞横幅，见 EventApplyService。改 cut-in 规则只改本文件。
    // =========================================================================

    /// <summary>一次取景 cut-in 的编译期注记（挂在 EventGroup.CutIn，null=不切）。</summary>
    public class CutInPlan
    {
        public string HeroId;
        public string Title;
        public int GroupId;
        /// <summary>满档加强出手（整组裂地拉满 + 强化音效）。</summary>
        public bool Empowered;
        /// <summary>巨伤「重创」（整组裂地拉满 ×1.5 + 强制震屏）。</summary>
        public bool Massive;
        /// <summary>武将专属高光（服务 hint.cut_in=highlight 点名取景）。</summary>
        public bool Highlight;
    }

    public static class CutInPlanner
    {
        /// <summary>巨伤 cut-in 门槛：单条 damage 超过即触发「重创」取景 cut-in。</summary>
        public const float HighDamageThreshold = 3000f;

        /// <summary>行动窗内第 N 次追击单元触发「追击不止」cut-in。</summary>
        public const int PursuitCutInAt = 5;

        /// <summary>势能满档值（与 MomentumService.Full 同义；预演侧独立持有，
        /// 该值由契约语义冻结而非 UI 配置）。</summary>
        public const int MomentumFull = 5;

        public static bool IsHighDamage(DamageEvent damage) =>
            damage != null && damage.Amount > HighDamageThreshold;

        /// <summary>编译期注记入口：按组序判定并写 EventGroup.CutIn。
        /// <paramref name="isKnownTrack"/> 为势能轨有效性谓词（L4 注入
        /// MomentumService.TrackTable，避免轨表双真源）。每局调用一次。</summary>
        public static void Annotate(List<EventGroup> groups, Func<string, bool> isKnownTrack)
        {
            var momentum = new Dictionary<(string hero, string track), int>();
            int pursuitCount = 0;
            foreach (var group in groups)
            {
                if (group.Root is ActionStartEvent) pursuitCount = 0;
                if (group.Kind == GroupKind.Pursuit) pursuitCount++;
                group.CutIn = Resolve(group, pursuitCount, momentum, isKnownTrack);
                // 判定完成后才把本组势能进账进预演镜像（判据＝落账前值）
                foreach (var ev in group.Events)
                    if (ev is MomentumChangeEvent m)
                        momentum[(m.HeroId, m.Track)] = m.Value;
            }
        }

        /// <summary>优先级：专属高光 &gt; 满档 &gt; 巨伤 &gt; 追击第 N 次；一组最多一次。</summary>
        static CutInPlan Resolve(EventGroup group, int pursuitCount,
                                 Dictionary<(string, string), int> momentum,
                                 Func<string, bool> isKnownTrack)
        {
            var highlight = FindHighlight(group);
            if (highlight != null)
                return new CutInPlan
                {
                    HeroId = highlight.ActorId, Title = $"{SkillNameOf(group)}！",
                    GroupId = highlight.GroupId, Highlight = true,
                };

            var full = FindFullTrackCutIn(group, momentum, isKnownTrack);
            if (full != null)
                return new CutInPlan
                {
                    HeroId = full.HeroId, Title = $"{SkillNameOf(group)}！",
                    GroupId = full.GroupId, Empowered = true,
                };

            var huge = FindHighDamage(group);
            if (huge != null)
                return new CutInPlan
                {
                    HeroId = huge.SourceId,
                    Title = $"{SkillNameOf(group)} 重创 {huge.TargetId}！-{huge.Amount}",
                    GroupId = huge.GroupId, Massive = true,
                };

            if (group.Kind == GroupKind.Pursuit && pursuitCount == PursuitCutInAt
                && group.Root is SkillTriggerEvent pst && !string.IsNullOrEmpty(pst.ActorId))
                return new CutInPlan
                {
                    HeroId = pst.ActorId, Title = "追击不止！",
                    GroupId = group.Root.GroupId,
                };
            return null;
        }

        /// <summary>武将专属高光（服务点名取景）：组根 skill_trigger 带
        /// hint.cut_in="highlight"。阈值不在客户端——「这一下算不算高光」是玩法语义，
        /// 由 core 判定并注记，客户端只按注记取景（宙斯神罚是第一例，后续核心卡逐个加）。</summary>
        static SkillTriggerEvent FindHighlight(EventGroup group) =>
            group.Root is SkillTriggerEvent st && st.HintOf("cut_in") == "highlight" ? st : null;

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

        /// <summary>「轨已满后再次进账」的满档 cut-in（出手前阻塞预播）。
        /// 预演镜像 ≥ Full(5) 且事件 cut_in——刚满 5 的当次不切；其他轨互不影响。</summary>
        static MomentumChangeEvent FindFullTrackCutIn(EventGroup group,
            Dictionary<(string, string), int> momentum, Func<string, bool> isKnownTrack)
        {
            foreach (var ev in group.Events)
            {
                if (ev is not MomentumChangeEvent { CutIn: true } m) continue;
                if (isKnownTrack != null && !isKnownTrack(m.Track)) continue;
                if (momentum.TryGetValue((m.HeroId, m.Track), out int v) && v >= MomentumFull)
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
