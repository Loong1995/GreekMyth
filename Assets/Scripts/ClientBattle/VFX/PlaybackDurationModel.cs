using System.Collections.Generic;
using ClientBattle.Events;
using ClientBattle.Units;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 离线时长模型】PlaybackDurationModel：编译产物 + 演出配置 → 各回合用时。
    //
    // 为什么能算准：播放时间轴上**每一拍的时长都是配置值**，没有一处依赖随机或
    // 玩家输入 ——
    //   - 编排层停顿：PerformanceRunner.ActionPauseSeconds / GroupPauseSeconds
    //     （× DurationMul/Speed，见 PlaybackDirector.Wait01）；
    //   - 出手三拍：StagePerformanceConfig.Windup/Strike/RecoverSeconds；
    //   - 弹道飞行/落雷/治疗间隔：DefaultPerformance 内的基准秒；
    //   - cut-in 取景：CutInCameraPushSeconds/HoldSeconds + CutInService.SoloRoutine；
    //   - 单挑：DuelPerformance + DuelStage 的分幕常量；
    //   - 台词独占：ChatBubbleService.ExclusiveSeconds。
    // 所以本类**不做启发式猜测**，而是逐组照抄演出协程的时长算术：模板判定与
    // DefaultPerformance.Play 同源（同一份 PerformanceProfile，由 VFXResolver 解析）。
    //
    // 已知不可离线求解的两项（都只在单挑里）：DuelStage.Burst / FireResultVfx
    // 等厂包粒子的真实发射窗（运行期 VFXManager.EmitWindow 探测，上限
    // DuelVfxWaitCap）。用 <see cref="PlaybackTimingOptions.DuelVfxPlaySeconds"/>
    // 取保底值，误差上限 = 2 ×(cap − fallback) ≈ 2.5s/次单挑。
    //
    // 另一项系统性偏差：所有 `for(t<dur) yield return null` 循环都会多跑到帧边界，
    // 平均每拍 +半帧。按拍数计入 <see cref="PlaybackTimingOptions.FrameSeconds"/>。
    //
    // 【模型按实测校准过一次】2026-07-28 用 PlaybackDirector.OnGroupPlayed 钩子录了
    // 一场 3v3 的逐组真值（122 组）对齐：台词/落雷/取景/被动/单挑分幕全部 1.00 吻合，
    // 唯独出手三拍的预备与收势对不上——DOTween `WaitForCompletion` 不等满时长
    // （P-84）。故这两拍按实测常数建模，见 TweenWait*Seconds。
    // 重新标定流程：钩子录 tsv → `battle/tools/compare_playback_timing.py` 比对。
    //
    // 文档：docs/client/playback_script.md §四.1
    // =========================================================================

    /// <summary>估算参数（默认＝PerformanceRunner Inspector 默认，即"正常速度"）。</summary>
    public class PlaybackTimingOptions
    {
        public float DurationMul = 2f;
        public float Speed = 1f;
        public float ActionPauseSeconds = 0.55f;
        public float GroupPauseSeconds = 0.35f;
        /// <summary>单挑出阵/胜负特效的真实发射窗（运行期 EmitWindow 探测，上限
        /// DuelVfxWaitCap=1.7）。离线取 2026-07-28 实测值。</summary>
        public float DuelVfxPlaySeconds = 1.2f;
        /// <summary>帧边界补偿：每拍平均多跑半帧（60fps → 1/120s）。</summary>
        public float FrameSeconds = 1f / 120f;

        public static PlaybackTimingOptions FromPacing(IPlaybackPacing pacing) =>
            pacing == null
                ? new PlaybackTimingOptions()
                : new PlaybackTimingOptions
                {
                    DurationMul = pacing.DurationMul,
                    Speed = pacing.Speed,
                    ActionPauseSeconds = pacing.ActionPauseSeconds,
                    GroupPauseSeconds = pacing.GroupPauseSeconds,
                };
    }

    /// <summary>一个回合的用时（`RoundNo=-1` ＝首个 round_start 之前的开场）。
    /// <see cref="GameIndex"/>+<see cref="StartSeq"/> 供「点时间轴跳到该回合」用
    /// （`PerformanceRunner.PlayFrom`）；<see cref="StartSeconds"/> 是本回合在
    /// 整场时间轴上的起点秒数。</summary>
    public readonly struct RoundTiming
    {
        public readonly int GameNo, GameIndex, RoundNo, StartSeq, GroupCount, ActionCount;
        public readonly float Seconds, StartSeconds, PauseSeconds;

        public RoundTiming(int gameNo, int gameIndex, int roundNo, int startSeq,
                           float startSeconds, float seconds, float pauseSeconds,
                           int groupCount, int actionCount)
        {
            GameNo = gameNo;
            GameIndex = gameIndex;
            RoundNo = roundNo;
            StartSeq = startSeq;
            StartSeconds = startSeconds;
            Seconds = seconds;
            PauseSeconds = pauseSeconds;
            GroupCount = groupCount;
            ActionCount = actionCount;
        }
    }

    public static class PlaybackDurationModel
    {
        // ---------------------------------------------------------- 主入口

        /// <summary>逐局逐回合累计用时。<paramref name="resolver"/> 必须与运行期同一份
        /// PerformanceDatabase，否则专配模板（RemoteStrike/OracleAura/None）会算错。</summary>
        public static List<RoundTiming> Rounds(CompiledPlayback compiled, VFXResolver resolver,
                                              PlaybackTimingOptions opt = null)
        {
            opt ??= new PlaybackTimingOptions();
            resolver ??= new VFXResolver(null);
            var result = new List<RoundTiming>();
            float pause = Pace(opt); // 编排层停顿：× DurationMul/Speed（无 TempoScale）

            float timeline = 0f; // 整场时间轴游标（跨局累计）
            for (int gi = 0; gi < compiled.GameGroups.Count; gi++)
            {
                int gameNo = compiled.Report.Games[gi].GameNo;
                var groups = compiled.GameGroups[gi];
                int gameIndex = gi;
                int roundNo = -1;
                int startSeq = groups.Count > 0 ? groups[0].RootSeq : 0;
                float startSec = timeline;
                float acc = 0f, pauseAcc = 0f;
                int gCount = 0, aCount = 0;
                bool acted = false;

                void Flush()
                {
                    if (gCount == 0 && acc < 0.01f) return;
                    result.Add(new RoundTiming(gameNo, gameIndex, roundNo, startSeq,
                                               startSec, acc, pauseAcc, gCount, aCount));
                    timeline += acc;
                    startSec = timeline;
                    acc = 0f;
                    pauseAcc = 0f;
                    gCount = 0;
                    aCount = 0;
                }

                for (int i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    bool isRoundStart = group.Root is RoundStartEvent;
                    // 回合边界停顿归**上一回合**（Director 在读到 round_start 时才等）
                    if (isRoundStart && acted && opt.ActionPauseSeconds > 0f)
                    {
                        float fade = opt.ActionPauseSeconds * pause;
                        acc += fade;
                        pauseAcc += fade;
                    }
                    if (isRoundStart)
                    {
                        Flush();
                        roundNo = ((RoundStartEvent)group.Root).RoundNo;
                        startSeq = group.RootSeq;
                        acted = false;
                    }
                    else if (group.Root is ActionStartEvent && acted)
                    {
                        acted = false;
                        if (opt.ActionPauseSeconds > 0f)
                        {
                            float gap = opt.ActionPauseSeconds * pause;
                            acc += gap;
                            pauseAcc += gap;
                        }
                    }

                    gCount++;
                    if (group.ParallelWithNext) continue; // 静默落账，不占时间轴

                    acc += GroupSeconds(group, resolver, opt);

                    if (GroupKinds.IsAction(group.Kind))
                    {
                        acted = true;
                        aCount++;
                        bool nextTrait = i + 1 < groups.Count
                                         && groups[i + 1].Kind == GroupKind.TraitLine
                                         && !groups[i + 1].ParallelWithNext;
                        if (opt.GroupPauseSeconds > 0f && !nextTrait)
                        {
                            float gap = opt.GroupPauseSeconds * pause;
                            acc += gap;
                            pauseAcc += gap;
                        }
                    }
                }
                Flush();
            }
            return result;
        }

        // ---------------------------------------------------------- 单组

        /// <summary>一个播放单元的时长（镜像 PlaybackDirector.PlayGroup 的分派）。</summary>
        public static float GroupSeconds(EventGroup group, VFXResolver resolver,
                                        PlaybackTimingOptions opt)
        {
            switch (group.Kind)
            {
                case GroupKind.Node:
                case GroupKind.StatusChange:
                case GroupKind.Defeat:
                    return 0f; // 即时落账，不占时间轴
                case GroupKind.TraitLine:
                    return TraitLineSeconds(group, opt);
                case GroupKind.Duel:
                    return DuelSeconds(group, opt);
                default:
                    return SkillGroupSeconds(group, resolver.Resolve(group), opt);
            }
        }

        static float TraitLineSeconds(EventGroup group, PlaybackTimingOptions opt)
        {
            // SayExclusive 内部已按 DurationMul/Speed 缩放并返回阻塞秒数
            int lines = 0;
            foreach (var ev in group.All<TraitTriggerEvent>())
                if (!string.IsNullOrEmpty(ev.Line)) lines++;
            return lines * ChatBubbleService.ExclusiveSeconds * Pace(opt);
        }

        /// <summary>战法/普攻/追击/状态触发组：cut-in 取景 + 演出模板本体。</summary>
        static float SkillGroupSeconds(EventGroup group, PerformanceProfile profile,
                                       PlaybackTimingOptions opt)
        {
            if (profile.Template == PerformanceTemplate.None) return 0f;

            // 连发第 2 发起组内节拍加速（ctx.TempoScale = BurstTempoScale）
            float tempo = group.Root is SkillTriggerEvent { BurstNo: >= 2 }
                ? Mathf.Max(1f, profile.BurstTempoScale)
                : 1f;

            if (group.Root is SkillTriggerEvent { Kind: "delayed" })
                return profile.DelayedAnnouncePause * Pace(opt, tempo);

            float body = profile.Template == PerformanceTemplate.OracleAura
                ? 0f // OracleAuraPerformance 不 yield（同帧落账）
                : DefaultPerformanceSeconds(group, profile, opt, tempo);

            if (group.CutIn != null) body += CutInStageSeconds(opt, tempo);
            return body;
        }

        /// <summary>镜像 DefaultPerformance.Play 的时长算术（模板判定同源）。</summary>
        static float DefaultPerformanceSeconds(EventGroup group, PerformanceProfile profile,
                                               PlaybackTimingOptions opt, float tempo)
        {
            var damages = group.All<DamageEvent>();
            var heals = group.All<HealEvent>();
            if (damages.Count == 0 && heals.Count == 0) return 0f; // 纯宣告：只飘字

            float s = 0f;
            int beats = 0;
            if (damages.Count == 0)
            {
                // 纯治疗组：每段 WaitForSeconds(0.3)
                s += heals.Count * Scaled(HealBeatSeconds, opt, tempo);
                return s + heals.Count * opt.FrameSeconds;
            }

            var template = ResolveTemplate(group, profile, damages);
            if (!string.IsNullOrEmpty(profile.CastKey) && template == PerformanceTemplate.Melee)
            {
                s += Scaled(CastBeatSeconds, opt, tempo);
                beats++;
            }

            switch (template)
            {
                case PerformanceTemplate.Melee:
                    // 逐段：预备 + 突进 + 收势（命中拍与突进末帧同帧，不额外占时）
                    foreach (var _ in damages)
                    {
                        s += AdvanceSeconds(opt, tempo) + RecoverSeconds(opt, tempo);
                        beats += 4;
                    }
                    break;
                case PerformanceTemplate.AoeCenter:
                    // 进中心 → 齐射飞行 → 收势（错峰只提前起飞，不加总时长）
                    s += AdvanceSeconds(opt, tempo)
                         + Scaled(AoeFlightSeconds, opt, tempo)
                         + RecoverSeconds(opt, tempo);
                    beats += 5;
                    break;
                case PerformanceTemplate.RemoteStrike:
                    s += Scaled(RemoteMarkSeconds, opt, tempo)
                         + Scaled(RemoteStrikeSeconds, opt, tempo);
                    beats += 2;
                    break;
                default: // PerSegment：一段一条弹道
                    foreach (var _ in damages)
                    {
                        s += Scaled(SegmentFlightSeconds, opt, tempo);
                        beats++;
                    }
                    break;
            }

            // 模板内未处理的治疗（收尾统一 SettleHeal）
            s += heals.Count * Scaled(HealBeatSeconds, opt, tempo);
            beats += heals.Count;
            return s + beats * opt.FrameSeconds;
        }

        /// <summary>与 DefaultPerformance.Play 里的模板判定逐条对应（含战法标签覆盖）。</summary>
        static PerformanceTemplate ResolveTemplate(EventGroup group, PerformanceProfile profile,
                                                   List<DamageEvent> damages)
        {
            var template = profile.Template;
            if (template != PerformanceTemplate.Auto && template != PerformanceTemplate.StatusTrigger)
                return template;

            var ids = new HashSet<string>();
            foreach (var d in damages) ids.Add(d.TargetId);
            int distinct = ids.Count;
            bool melee = group.Kind == GroupKind.NormalAttack
                         || (group.Kind == GroupKind.Pursuit && distinct <= 1);
            template = melee ? PerformanceTemplate.Melee
                : distinct >= 2 ? PerformanceTemplate.AoeCenter
                                : PerformanceTemplate.PerSegment;
            if (!melee && group.ForcePerTarget) template = PerformanceTemplate.PerSegment;
            else if (!melee && group.ForceSimultaneous && damages.Count > 1)
                template = PerformanceTemplate.AoeCenter;
            return template;
        }

        // ---------------------------------------------------------- 取景 / 单挑

        /// <summary>CutInStage：推镜 → 定住（真实秒）→ 斜带立绘 → 撤镜。</summary>
        static float CutInStageSeconds(PlaybackTimingOptions opt, float tempo)
        {
            float push = Scaled(StagePerformanceConfig.CutInCameraPushSeconds, opt, tempo);
            float solo = Scaled(SoloInSeconds + SoloHoldSeconds + SoloOutSeconds, opt, tempo);
            return push * 2f + StagePerformanceConfig.CutInCameraHoldSeconds + solo
                   + 6 * opt.FrameSeconds;
        }

        /// <summary>DuelPerformance + DuelStage 分幕累计。</summary>
        static float DuelSeconds(EventGroup group, PlaybackTimingOptions opt)
        {
            var challenge = group.First<DuelChallengeEvent>();
            if (challenge == null) return 0f;
            var result = group.First<DuelResultEvent>();
            float pace = Pace(opt);
            float line = ChatBubbleService.ExclusiveSeconds * pace;

            float s = UnitView.DimFadeSeconds * pace   // 压暗渐变
                      + DuelHornSeconds * pace;        // 号角后的呼吸
            s += LinesOf(group, "duel_challenge") * line;

            if (result != null && !result.Accepted)
            {
                s += LinesOf(group, "duel_reject") * line + DuelRejectTailSeconds * pace;
            }
            else if (result != null)
            {
                s += LinesOf(group, "duel_accept") * line;
                s += DuelStageSeconds(Mathf.Clamp(challenge.ClashCutins, 1, 3), opt);
                s += DuelWinBannerSeconds * pace + DuelPenaltySeconds * pace;
            }
            return s + UnitView.DimFadeSeconds * pace; // 解除压暗
        }

        static float DuelStageSeconds(int passes, PlaybackTimingOptions opt)
        {
            float s = Scaled(StagePerformanceConfig.DuelAnticipateSeconds, opt, 1f)
                      + opt.DuelVfxPlaySeconds                       // ① 出阵爆点（真实秒）
                      + Scaled(StagePerformanceConfig.DuelCameraPushSeconds, opt, 1f)
                      + StagePerformanceConfig.DuelCameraHoldSeconds  // 定住（真实秒）
                      + Scaled(StagePerformanceConfig.DuelFlySeconds, opt, 1f)
                      + Scaled(StagePerformanceConfig.DuelIconSeconds, opt, 1f);
            for (int i = 0; i < passes; i++)
            {
                bool last = i == passes - 1;
                if (last && passes > 1)
                    s += Scaled(StagePerformanceConfig.DuelBraceSeconds, opt, 1f);
                s += Scaled(StagePerformanceConfig.DuelCrossSeconds, opt, 1f)
                     + Scaled(StagePerformanceConfig.DuelCrossSeconds
                              * StagePerformanceConfig.DuelCrossReturnRatio, opt, 1f)
                     + Scaled(StagePerformanceConfig.DuelActionSeconds
                              * (last ? StagePerformanceConfig.DuelFinalRoundScale : 1f), opt, 1f);
            }
            s += Scaled(StagePerformanceConfig.DuelResultSeconds, opt, 1f)
                 + Scaled(StagePerformanceConfig.DuelResultHoldSeconds, opt, 1f)
                 + Scaled(StagePerformanceConfig.DuelFlySeconds, opt, 1f)      // 回框
                 + Scaled(StagePerformanceConfig.DuelCameraPushSeconds, opt, 1f) // 撤镜
                 + opt.DuelVfxPlaySeconds;                                     // ⑧ 胜负特效
            return s + (10 + passes * 3) * opt.FrameSeconds;
        }

        static int LinesOf(EventGroup group, string effect)
        {
            int n = 0;
            foreach (var ev in group.All<TraitTriggerEvent>())
                if (ev.Effect == effect && !string.IsNullOrEmpty(ev.Line)) n++;
            return n;
        }

        // ---------------------------------------------------------- 常量（演出真源镜像）

        const float CastBeatSeconds = 0.22f;      // DefaultPerformance：Melee 前 CastKey
        const float AoeFlightSeconds = 0.38f;     // 群攻齐射飞行
        const float SegmentFlightSeconds = 0.30f; // 逐段弹道飞行
        const float RemoteMarkSeconds = 0.06f;    // 落雷前头像标
        const float RemoteStrikeSeconds = 0.42f;  // 竖雷落劈
        const float HealBeatSeconds = 0.3f;       // 每段治疗间隔
        const float SoloInSeconds = 0.16f;        // CutInService.SoloRoutine dIn
        const float SoloHoldSeconds = 0.5f;       // dHold
        const float SoloOutSeconds = 0.14f;       // dOut
        const float DuelHornSeconds = 0.55f;      // 号角横幅后
        const float DuelRejectTailSeconds = 0.5f;
        const float DuelWinBannerSeconds = 0.8f;
        const float DuelPenaltySeconds = 0.5f;

        /// <summary>出手三拍的**实际**阻塞时长（2026-07-28 逐拍实测，P-84）。
        ///
        /// 预备与收势都靠 DOTween `tween.WaitForCompletion()` 等待，而它**不等满
        /// 时长**：实测 0.24s 的预备只阻塞 ~0.05s、0.52s 的收势只阻塞 ~0.10s
        /// （位移仍在后台继续，于是与下一拍重叠）。只有中间那一拍
        /// （TrailWhile 自走时钟）是足额的 `StrikeSeconds`。
        ///
        /// 所以本模型**按实测建模而不是按配置建模**——照配置算会把每段近身
        /// 高估约一倍（曾把 4 分钟战报算成 +14%）。若哪天把三拍改成真阻塞
        /// （产品决定），这里连同 <see cref="TweenWaitWindupSeconds"/> 一起回到配置值。</summary>
        const float TweenWaitWindupSeconds = 0.05f;
        const float TweenWaitRecoverSeconds = 0.10f;

        static float AdvanceSeconds(PlaybackTimingOptions opt, float tempo) =>
            TweenWaitWindupSeconds
            + Scaled(StagePerformanceConfig.StrikeSeconds, opt, tempo);

        static float RecoverSeconds(PlaybackTimingOptions opt, float tempo)
        {
            _ = opt;
            _ = tempo;
            return TweenWaitRecoverSeconds; // tween 不等满，与倍率无关（实测）
        }

        /// <summary>VFXContext.Scaled 的离线版。</summary>
        static float Scaled(float seconds, PlaybackTimingOptions opt, float tempo) =>
            seconds * Mathf.Max(0.1f, opt.DurationMul)
            / Mathf.Max(0.1f, opt.Speed * Mathf.Max(0.1f, tempo));

        /// <summary>编排层停顿与气泡的缩放（无 TempoScale，见 Director.Wait01）。</summary>
        static float Pace(PlaybackTimingOptions opt, float tempo = 1f) =>
            Mathf.Max(0.1f, opt.DurationMul) / Mathf.Max(0.1f, opt.Speed * Mathf.Max(0.1f, tempo));

        // ---------------------------------------------------------- 输出

        public static JObject ToJson(IReadOnlyList<RoundTiming> rounds, PlaybackTimingOptions opt)
        {
            var arr = new JArray();
            var byGame = new Dictionary<int, float>();
            float total = 0f;
            foreach (var r in rounds)
            {
                total += r.Seconds;
                byGame.TryGetValue(r.GameNo, out float g);
                byGame[r.GameNo] = g + r.Seconds;
                var row = new JObject
                {
                    ["game_no"] = r.GameNo,
                    ["round_no"] = r.RoundNo,
                    ["start_seq"] = r.StartSeq,
                    ["start_sec"] = Mathf.Round(r.StartSeconds * 10f) / 10f,
                    ["est_sec"] = Mathf.Round(r.Seconds * 10f) / 10f,
                    ["pause_sec"] = Mathf.Round(r.PauseSeconds * 10f) / 10f,
                    ["groups"] = r.GroupCount,
                    ["actions"] = r.ActionCount,
                };
                if (r.RoundNo < 0) row["label"] = "开场";
                arr.Add(row);
            }
            var games = new JArray();
            foreach (var kv in byGame)
                games.Add(new JObject
                {
                    ["game_no"] = kv.Key,
                    ["est_sec"] = Mathf.Round(kv.Value * 10f) / 10f,
                });
            return new JObject
            {
                ["model"] = "analytic-v2",
                ["duration_mul"] = opt.DurationMul,
                ["speed"] = opt.Speed,
                ["action_pause_sec"] = opt.ActionPauseSeconds,
                ["group_pause_sec"] = opt.GroupPauseSeconds,
                ["note"] = "逐拍取自演出配置 + 出手三拍按实测（P-84）；"
                           + "已对真播逐组标定，实测逐回合误差 ≤1.2s",
                ["total_est_sec"] = Mathf.Round(total * 10f) / 10f,
                ["games"] = games,
                ["rounds"] = arr,
            };
        }

        public static string FormatSummary(IReadOnlyList<RoundTiming> rounds,
                                          PlaybackTimingOptions opt)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[PlaybackTiming] 解析模型 v2（DurationMul={opt.DurationMul}，"
                          + $"Speed={opt.Speed}，行动停顿 {opt.ActionPauseSeconds}s，"
                          + $"单元停顿 {opt.GroupPauseSeconds}s）");
            int prevGame = int.MinValue;
            float gameSum = 0f, total = 0f;
            foreach (var r in rounds)
            {
                if (r.GameNo != prevGame)
                {
                    if (prevGame != int.MinValue)
                        sb.AppendLine($"  ── 第{prevGame}局合计 {gameSum:0.0}s（{gameSum / 60f:0.0} min）");
                    prevGame = r.GameNo;
                    gameSum = 0f;
                    sb.AppendLine($"第 {r.GameNo} 局：");
                }
                gameSum += r.Seconds;
                total += r.Seconds;
                string label = r.RoundNo < 0 ? "开场    " : $"回合 {r.RoundNo,2}  ";
                sb.AppendLine($"  {label}{r.Seconds,6:0.0}s   "
                              + $"(单元 {r.GroupCount,3} / 行动 {r.ActionCount,2} / "
                              + $"其中停顿 {r.PauseSeconds,5:0.0}s)");
            }
            if (prevGame != int.MinValue)
                sb.AppendLine($"  ── 第{prevGame}局合计 {gameSum:0.0}s（{gameSum / 60f:0.0} min）");
            sb.AppendLine($"全系列 {total:0.0}s（{total / 60f:0.0} min）");
            return sb.ToString();
        }
    }
}
