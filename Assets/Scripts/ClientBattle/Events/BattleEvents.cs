using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClientBattle.Events
{
    // =========================================================================
    // 【第1层 事件模型】战报事件类 + BattleEventParser
    //
    // 设计约定（与后端契约 docs/schema/battle_events.md 对齐，schema 1.4.1）：
    // - 信封字段（seq/t/type/parent_seq/group_id）强类型；payload 逐类型解析成派生类字段。
    // - 未知事件类型 → UnknownEvent（RawPayload 保留），向前兼容义务：跳过但不中断播放。
    // - 派生类只做"读取"不做任何结算——客户端红线：一切数值以事件为准。
    // =========================================================================

    /// <summary>逻辑时间 t=(g局,r回合,p相位,s槽位)，字典序与 seq 序一致。</summary>
    [Serializable]
    public struct LogicalTime
    {
        public int G, R, P, S;
        public override string ToString() => $"g{G} r{R} p{P} s{S}";
    }

    /// <summary>所有战报事件的基类：信封字段 + 原始 payload（向前兼容保底）。</summary>
    public abstract class BattleEvent
    {
        public int Seq;
        public LogicalTime T;
        public string Type;
        public int ParentSeq;
        public int GroupId;
        /// <summary>原始 payload，派生类没覆盖到的新字段从这里兜底读取。</summary>
        public JObject RawPayload;

        /// <summary>由派生类实现：从 payload 解析出强类型字段。</summary>
        protected internal abstract void Parse(JObject payload);
    }

    /// <summary>兵力变化四池明细（troops 字段通用结构）。</summary>
    public class TroopsDelta
    {
        public string HeroId;
        public int TroopsBefore, TroopsAfter;
        public int WoundedBefore, WoundedAfter;
        public int DeadBefore, DeadAfter;

        public static TroopsDelta From(JObject o)
        {
            if (o == null) return null;
            return new TroopsDelta
            {
                HeroId = o.Value<string>("hero_id"),
                TroopsBefore = o.Value<int>("troops_before"),
                TroopsAfter = o.Value<int>("troops_after"),
                WoundedBefore = o.Value<int>("wounded_before"),
                WoundedAfter = o.Value<int>("wounded_after"),
                DeadBefore = o.Value<int>("dead_before"),
                DeadAfter = o.Value<int>("dead_after"),
            };
        }
    }

    /// <summary>状态引用（status_id + owner_id + instance）。</summary>
    public class StatusRef
    {
        public string StatusId;
        public string OwnerId;

        public static StatusRef From(JObject o)
        {
            if (o == null) return null;
            return new StatusRef
            {
                StatusId = o.Value<string>("status_id"),
                OwnerId = o.Value<string>("owner_id"),
            };
        }
    }

    // ------------------------------------------------------------ 结算类事件

    public class DamageEvent : BattleEvent
    {
        public string SourceId, TargetId;
        public string DamageType;   // physical / magic / true
        public int Amount;
        public bool IsCrit;
        public TroopsDelta Troops;
        public string Mitigation;   // "" / "block" / "evade" / "reflect"（1.2.0 / 1.3.1 可选）
        public string DamageClass;  // "" / "special"（震荡等，1.2.0 可选）

        protected internal override void Parse(JObject p)
        {
            SourceId = p.Value<string>("source_id");
            TargetId = p.Value<string>("target_id");
            DamageType = p.Value<string>("damage_type");
            Amount = p.Value<int>("amount");
            IsCrit = p.Value<bool>("is_crit");
            Troops = TroopsDelta.From(p.Value<JObject>("troops"));
            Mitigation = p.Value<string>("mitigation") ?? "";
            DamageClass = p.Value<string>("damage_class") ?? "";
        }
    }

    public class HealEvent : BattleEvent
    {
        public string SourceId, TargetId;
        public int Amount;
        public bool IsCrit;
        public TroopsDelta Troops;

        protected internal override void Parse(JObject p)
        {
            SourceId = p.Value<string>("source_id");
            TargetId = p.Value<string>("target_id");
            Amount = p.Value<int>("amount");
            IsCrit = p.Value<bool>("is_crit");
            Troops = TroopsDelta.From(p.Value<JObject>("troops"));
        }
    }

    public class SkillTriggerEvent : BattleEvent
    {
        public string ActorId, SkillId, Kind; // kind: cast/prepare/release/interrupted/delayed/assist
        public List<string> TargetIds = new();
        public int BurstNo; // schema 1.4.0 可选：连发第 N 次释放（2 起，硬上限 7）；0=非连发

        protected internal override void Parse(JObject p)
        {
            ActorId = p.Value<string>("actor_id");
            SkillId = p.Value<string>("skill_id");
            Kind = p.Value<string>("kind");
            BurstNo = p.Value<int?>("burst_no") ?? 0;
            var ids = p.Value<JArray>("target_ids");
            if (ids != null) foreach (var id in ids) TargetIds.Add((string)id);
        }
    }

    public class NormalAttackEvent : BattleEvent
    {
        public string ActorId;
        public List<string> TargetIds = new();
        public int StrikeNo;
        public string Kind; // schema 1.4.0 可选："coordinated"=协击；null=普攻

        protected internal override void Parse(JObject p)
        {
            ActorId = p.Value<string>("actor_id");
            StrikeNo = p.Value<int>("strike_no");
            Kind = p.Value<string>("kind");
            var ids = p.Value<JArray>("target_ids");
            if (ids != null) foreach (var id in ids) TargetIds.Add((string)id);
        }
    }

    // ------------------------------------------------------------ 状态类事件

    public class StatusApplyEvent : BattleEvent
    {
        public StatusRef Status;
        public string SourceId;
        public int Stacks, DurationRounds;

        protected internal override void Parse(JObject p)
        {
            Status = StatusRef.From(p.Value<JObject>("status"));
            SourceId = p.Value<string>("source_id");
            Stacks = p.Value<int>("stacks");
            DurationRounds = p.Value<int>("duration_rounds");
        }
    }

    public class StatusRefreshEvent : StatusApplyEvent { }

    public class StatusRemoveEvent : BattleEvent
    {
        public StatusRef Status;
        public string Reason;

        protected internal override void Parse(JObject p)
        {
            Status = StatusRef.From(p.Value<JObject>("status"));
            Reason = p.Value<string>("reason");
        }
    }

    public class StatusTickEvent : BattleEvent
    {
        public StatusRef Status;
        public string SourceId;

        protected internal override void Parse(JObject p)
        {
            Status = StatusRef.From(p.Value<JObject>("status"));
            SourceId = p.Value<string>("source_id");
        }
    }

    public class AttrChangeEvent : BattleEvent
    {
        public struct Change { public string Attr; public int Before, After; }
        public string HeroId, Scope;
        public List<Change> Changes = new();

        protected internal override void Parse(JObject p)
        {
            HeroId = p.Value<string>("hero_id");
            Scope = p.Value<string>("scope");
            var arr = p.Value<JArray>("changes");
            if (arr == null) return;
            foreach (var c in arr)
                Changes.Add(new Change
                {
                    Attr = c.Value<string>("attr"),
                    Before = c.Value<int>("before"),
                    After = c.Value<int>("after"),
                });
        }
    }

    public class TroopsChangeEvent : BattleEvent
    {
        public string Reason;
        public TroopsDelta Troops;

        protected internal override void Parse(JObject p)
        {
            Reason = p.Value<string>("reason") ?? "";
            // payload = { reason, troops: TroopsDelta }，不可把整包当 TroopsDelta
            Troops = TroopsDelta.From(p.Value<JObject>("troops"));
        }
    }

    // ------------------------------------------------------------ 性格台词（推送即播）

    public class TraitTriggerEvent : BattleEvent
    {
        public string HeroId, TraitId, Effect, Line;

        protected internal override void Parse(JObject p)
        {
            HeroId = p.Value<string>("hero_id");
            TraitId = p.Value<string>("trait_id");
            Effect = p.Value<string>("effect");
            Line = p.Value<string>("line") ?? "";
        }
    }

    // ------------------------------------------------------------ 节点类事件

    public class RoundStartEvent : BattleEvent
    {
        public int RoundNo;
        protected internal override void Parse(JObject p) => RoundNo = p.Value<int>("round_no");
    }

    public class ActionStartEvent : BattleEvent
    {
        public string ActorId;
        public bool Skipped;

        protected internal override void Parse(JObject p)
        {
            ActorId = p.Value<string>("actor_id");
            Skipped = p.Value<bool>("skipped");
        }
    }

    public class HeroDefeatedEvent : BattleEvent
    {
        public string HeroId;
        public bool IsMainHero;

        protected internal override void Parse(JObject p)
        {
            HeroId = p.Value<string>("hero_id");
            IsMainHero = p.Value<bool>("is_main_hero");
        }
    }

    public class DuelChallengeEvent : BattleEvent
    {
        public string ChallengerId, DefenderId;
        public int ChallengerForce, DefenderForce;
        /// <summary>交锋 cut-in 段数（1~3）；缺省按 3（旧战报兼容）。</summary>
        public int ClashCutins = 3;

        protected internal override void Parse(JObject p)
        {
            ChallengerId = p.Value<string>("challenger_id");
            DefenderId = p.Value<string>("defender_id");
            ChallengerForce = p.Value<int>("challenger_force");
            DefenderForce = p.Value<int>("defender_force");
            var cutins = p.Value<int?>("clash_cutins");
            if (cutins is >= 1 and <= 3) ClashCutins = cutins.Value;
        }
    }

    public class DuelResultEvent : BattleEvent
    {
        public bool Accepted;
        public string WinnerId, LoserId;

        protected internal override void Parse(JObject p)
        {
            Accepted = p.Value<bool>("accepted");
            WinnerId = p.Value<string>("winner_id");
            LoserId = p.Value<string>("loser_id");
        }
    }

    /// <summary>round_end / game_start / game_end / battle_end / phase_start 等
    /// 纯节点事件：无需强类型字段，用 RawPayload 兜底。</summary>
    public class MarkerEvent : BattleEvent
    {
        protected internal override void Parse(JObject p) { }
    }

    /// <summary>momentum_change（schema 1.4.0，Phase 4 四轨势能）：纯表现记账。
    /// B 批接入 UI 前先强类型解析、静默跳过播放（不落 UnknownEvent 告警）。</summary>
    public class MomentumChangeEvent : BattleEvent
    {
        public string HeroId, Track, Reason;
        public int Delta, Value;
        public bool CutIn;

        protected internal override void Parse(JObject p)
        {
            HeroId = p.Value<string>("hero_id");
            Track = p.Value<string>("track");
            Reason = p.Value<string>("reason");
            Delta = p.Value<int>("delta");
            Value = p.Value<int>("value");
            CutIn = p.Value<bool?>("cut_in") ?? false;
        }
    }

    /// <summary>tactic_applied（schema 1.4.1，P4-C 经理人战术变更生效）：
    /// 非阻塞横幅播报 + 左侧战术栏更新（战术栏 UI 随联网客户端接入）。</summary>
    public class TacticAppliedEvent : BattleEvent
    {
        public string TeamId, TacticId;
        public int RoundNo, ChangeNo;

        protected internal override void Parse(JObject p)
        {
            TeamId = p.Value<string>("team_id");
            TacticId = p.Value<string>("tactic_id");
            RoundNo = p.Value<int>("round_no");
            ChangeNo = p.Value<int>("change_no");
        }
    }

    /// <summary>未知事件类型（向前兼容）：跳过播放，仅日志提示。</summary>
    public class UnknownEvent : BattleEvent
    {
        protected internal override void Parse(JObject p) { }
    }

    // =========================================================================
    // BattleEventParser：JSON 事件数组 → List<BattleEvent>（type 字段多态分发）
    // =========================================================================

    public static class BattleEventParser
    {
        static readonly Dictionary<string, Func<BattleEvent>> Factory = new()
        {
            ["damage"] = () => new DamageEvent(),
            ["heal"] = () => new HealEvent(),
            ["skill_trigger"] = () => new SkillTriggerEvent(),
            ["normal_attack"] = () => new NormalAttackEvent(),
            ["status_apply"] = () => new StatusApplyEvent(),
            ["status_refresh"] = () => new StatusRefreshEvent(),
            ["status_remove"] = () => new StatusRemoveEvent(),
            ["status_tick"] = () => new StatusTickEvent(),
            ["attr_change"] = () => new AttrChangeEvent(),
            ["troops_change"] = () => new TroopsChangeEvent(),
            ["trait_trigger"] = () => new TraitTriggerEvent(),
            ["round_start"] = () => new RoundStartEvent(),
            ["action_start"] = () => new ActionStartEvent(),
            ["hero_defeated"] = () => new HeroDefeatedEvent(),
            ["duel_challenge"] = () => new DuelChallengeEvent(),
            ["duel_result"] = () => new DuelResultEvent(),
            ["round_end"] = () => new MarkerEvent(),
            ["game_start"] = () => new MarkerEvent(),
            ["game_end"] = () => new MarkerEvent(),
            ["battle_end"] = () => new MarkerEvent(),
            ["phase_start"] = () => new MarkerEvent(),
            ["momentum_change"] = () => new MomentumChangeEvent(),
            ["tactic_applied"] = () => new TacticAppliedEvent(),
        };

        /// <summary>解析单局 events 数组。未知 type 产出 UnknownEvent 并 LogWarning。</summary>
        public static List<BattleEvent> Parse(JArray eventsJson)
        {
            var result = new List<BattleEvent>(eventsJson.Count);
            foreach (var token in eventsJson)
            {
                var obj = (JObject)token;
                string type = obj.Value<string>("type");
                BattleEvent ev = Factory.TryGetValue(type, out var make) ? make() : new UnknownEvent();
                if (ev is UnknownEvent)
                    Debug.LogWarning($"[ClientBattle] 未知事件类型 '{type}'（seq={obj.Value<int>("seq")}），按向前兼容跳过播放");

                ev.Seq = obj.Value<int>("seq");
                ev.Type = type;
                ev.ParentSeq = obj.Value<int>("parent_seq");
                ev.GroupId = obj.Value<int>("group_id");
                var t = obj.Value<JObject>("t");
                ev.T = new LogicalTime
                {
                    G = t.Value<int>("g"), R = t.Value<int>("r"),
                    P = t.Value<int>("p"), S = t.Value<int>("s"),
                };
                ev.RawPayload = obj.Value<JObject>("payload") ?? new JObject();
                ev.Parse(ev.RawPayload);
                result.Add(ev);
            }
            return result;
        }
    }
}
