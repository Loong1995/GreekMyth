using System;
using System.Collections.Generic;
using ClientBattle.Audio;
using ClientBattle.Events;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 播放核心】PlaybackWorldBuilder：建世界的唯一实现。
    //
    // 产出一个完整装配的 PlaybackSession：棋盘/单位、事件管线、演出模板、
    // VFXContext（含全部编排回调注入）、镜像账本清零、报告驱动预热、BGM 起播。
    // 建世界前调用方必须已 HardStop（残留清理不是本类职责）。
    // =========================================================================

    public static class PlaybackWorldBuilder
    {
        public static PlaybackSession Build(
            BattleReport report,
            PerformanceDatabase database,
            BattleBoardView board,
            IPlaybackPacing pacing,
            Action<DamageEvent, string> onDamageSettled)
        {
            var s = new PlaybackSession { Report = report, Board = board };

            s.Resolver = new VFXResolver(database);
            s.Pipeline = new EventPipeline()
                // 借刀（代战/披甲）按段拆单元并回插事件流原生位置：
                // 段1(借手)→响应→追伤→段2…（不拆会三刀连劈再补账）
                .Register(new BorrowBladeSplitProcessor(
                    g => s.Resolver.Resolve(g).BorrowBlade))
                .Register(new ReactionRegroupProcessor())        // 状态触发摘出，排主单元之后
                .Register(new CollectiveTriggerMergeProcessor()) // 雷霆等合并为一次集体齐发
                .Register(new TraitLineExtractProcessor())       // 台词拆成独占 TraitLine 组
                .Register(new AchillesPierceTagProcessor())      // 傲慢贯穿 → 裂甲图标闸门
                .Register(new NodeMergeProcessor());

            s.DefaultPerf = ScriptableObject.CreateInstance<DefaultPerformance>();
            s.OraclePerf = ScriptableObject.CreateInstance<OracleAuraPerformance>();
            s.DuelPerf = ScriptableObject.CreateInstance<DuelPerformance>();

            // 分辨率/宽高比自适配（不同机型统一由 CameraFitter 权威取景）
            CameraFitter.EnsureOn(Camera.main);
            board.Build(report);

            var vfx = VFXManager.Ensure();
            vfx.Prewarm(); // 渲染级预热：shader/贴图/粒子网格全部压进加载期
            var floats = FloatingTextService.Ensure();
            var banner = BannerService.Ensure();
            var cutIn = CutInService.Ensure();

            var ctx = new VFXContext
            {
                Board = board,
                Vfx = vfx,
                Floats = floats,
                Sfx = SfxManager.Ensure(),
                Bubbles = ChatBubbleService.Ensure(),
                CutIns = cutIn,
                SpeedScale = pacing.Speed,
                DurationMul = pacing.DurationMul,
                // 编排层回调注入：演出执行层（SkillPerformance 族）零控制器依赖
                OnDamageSettled = onDamageSettled,
                OnBanner = banner.Set,
                OnBgmDuck = () => BgmLayerService.Instance?.Duck(),
            };
            ctx.OnCutInRequested = (heroId, text, groupId) =>
                cutIn.Request(ctx, heroId, text, groupId);
            s.Ctx = ctx;

            // 领域账本（MomentumService）与 Audio 层解耦：编排层接线
            MomentumService.GlobalMomentumChanged =
                total => BgmLayerService.Instance?.SetGlobalMomentum(total);
            // 会话建立即复位：势能镜像/光环/cut-in 组去重（重播同战报必撞相同
            // group_id，不复位会吞掉高伤/满档切入；主循环只在 gameIdx>0 清）
            MomentumService.ClearAll();
            UnitAuraService.ClearAll();
            cutIn.ResetDedup();

            PrewarmFromReport(s, database, floats); // 字形/音效/图标按本场战报内容前置生成
            BgmLayerService.Ensure().StartBattle(); // B3：stem/占位单曲同相位起播
            return s;
        }

        /// <summary>报告驱动预热：扫一遍战报事件，把战斗中会"第一次"产生分配或
        /// 纹理生成的东西（台词字形、名字字形、状态图标、合成音效、气泡对象）
        /// 全部在开战前做完。战斗热路径里从此只剩查缓存。</summary>
        static void PrewarmFromReport(PlaybackSession s, PerformanceDatabase database,
                                      FloatingTextService floats)
        {
            var text = new System.Text.StringBuilder();
            var statusIds = new HashSet<string>();
            var sfxKeys = new HashSet<string>
            {
                "sfx_melee_default", "sfx_pursuit_default", "sfx_status_trigger_default",
                "sfx_active_default", "sfx_hit_default", "sfx_heal_default", "sfx_oracle_default",
                "sfx_defeated", "sfx_duel_horn", "sfx_duel_clash", "sfx_duel_win", "sfx_petrify_off",
                "sfx_cutin_solo", "sfx_attack_empowered",
            };
            text.Append("VS势能全开追击不止重创单挑"); // cut-in 固定文案字形预热

            foreach (var team in s.Report.Teams)
                foreach (var hero in team.Heroes)
                    text.Append(hero.HeroId);
            foreach (var game in s.Report.Games)
                foreach (var ev in game.Events)
                    switch (ev)
                    {
                        case TraitTriggerEvent trait:
                            text.Append(trait.Line); break;
                        case StatusApplyEvent apply when apply.Status != null:
                            statusIds.Add(apply.Status.StatusId); break;
                        case DuelChallengeEvent duel:
                            text.Append(duel.ChallengerId).Append(duel.DefenderId); break;
                    }
            foreach (var id in statusIds)
            {
                StatusIconPanel.PrewarmIcon(id);
                sfxKeys.Add($"sfx_status_{id}");
            }
            // 特殊演出配置里的自定义音效 key 一并合成
            if (database != null)
                foreach (var profile in database.SpecialProfiles)
                {
                    if (!string.IsNullOrEmpty(profile.SfxKey)) sfxKeys.Add(profile.SfxKey);
                    if (!string.IsNullOrEmpty(profile.HitSfxKey)) sfxKeys.Add(profile.HitSfxKey);
                }
            foreach (var key in sfxKeys)
                Placeholder.PlaceholderFactory.GetAudio(key);

            floats.Prewarm(24, text.ToString()); // 飘字池 + 全部动态文本字形
            s.Ctx.Bubbles.Prewarm();             // 气泡对象与底图前置创建
        }
    }
}
