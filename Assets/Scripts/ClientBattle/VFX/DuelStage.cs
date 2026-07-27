using System.Collections;
using System.Collections.Generic;
using ClientBattle.Placeholder;
using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第4层 演出执行】单挑舞台 cut-in（2026-07-27 重做，替代旧「半屏卡掠过裂缝」）。
    //
    // 一句话：**把两名武将从各自卡框里揪出来，扔进一块虚空展示屏里打完，再送回卡框。**
    //
    // 分幕（四个**情感爆点**串起来的起承转合，不是四段等速位移）：
    //   ⓪ 蓄    立绘先往卡里陷一点（Pose 走负值）。预备动作是爆发力的唯一来源；
    //           少了这 0.16 秒，后面飞多快都只读作"位移"。
    //   ★爆点1 放：两人**定位圆**上同时炸开出阵特效 + 震屏 + 白闪。此刻暗幕**还没**
    //           压下（DuelVeilDelay），所以这一炸是在真战场上看见的——观众因此
    //           把随后出现的屏理解成"被拉进去的地方"，而不是一张凭空的贴图。
    //   ① 出框  两张立绘从卡面**当前世界姿态**起飞（无缝：起点就是卡上那张图），
    //           OutBack 过冲后收住，落到展示屏左右槽位；同时暗幕压下、展示屏展开、
    //           **相机推近并把俯角抬到垂直卡面**（StageCameraRig）。
    //           "人飞出来"与"镜头压过来"共用同一条进度，是一个动作的两面。
    //   ② 亮屏  中央单挑图标浮现（Resources/ClientBattle/UI/duel_icon，缺图走占位块）。
    //   ③ 交错×N 每轮＝**交错 → 归位 → 播放动作**，N ＝ 服务端 clash_cutins（1~3）。
    //           交错：两人对穿而过（一上弧一下弧才读得出"错身"，正对穿会糊成重叠），
    //           中点白闪 + onClash（音效/震屏由编排层给）；随后弹回各自槽位。
    //           播放：本轮**攻方**放 strike 序列、**守方**放 react 序列（末轮双方
    //           同时 strike＝对攻高潮）。攻守逐轮轮换，避免三轮长得一模一样。
    //   ★爆点2 末轮之前"静滞"一下（两人后撤、图标缩紧）。没有这口气，三轮交错
    //           就是等速流水账，最后一击也就不成其为最后一击。
    //   ★爆点3 ④ 定胜负：胜者提亮上前、败者压暗后仰。
    //   ★爆点4 ⑤ 回框：暗幕提前散、镜头撤回原位，立绘沿原路飞回；回程后段
    //           （DuelResultVfxCue）**胜者卡面加冕、败者定位圆留痕**——
    //           立绘是"落进自己的特效里"，而不是先落地再补一个特效。
    //
    // 【动作素材＝flipbook（逐帧图序列），不是视频】
    //   路径 Resources/ClientBattle/DuelAction/{template_id}_{strike|react}_{NN}.png
    //   （NN 从 00 连号，断号即停，上限 64 帧）。选 flipbook 不选 VideoPlayer 的原因：
    //     · 单挑要**两人同屏同时播**，移动端双路 VideoPlayer 解码是实打实的风险；
    //     · flipbook 是按 `ctx.Scaled` 走的帧下标，**天然吃倍速**；VideoPlayer 的
    //       playbackSpeed 与我们的时间轴是两套时钟，2×/4× 下必然对不上。
    //   缺帧回退（占位三级回退的本地实例）：整段序列缺失 → **退化为静态立绘单帧**，
    //   即"图片在 cut-in 屏上占满这段时间"，时序分毫不变。所以美术资源可以后补，
    //   补一个武将亮一个，不需要等齐。
    //
    // 【职责边界】本类只管**编排**：谁什么时候飞到哪、打哪一拍。屏怎么好看
    // （暗幕/屏体/阵营辉光/放射光芒/浮尘/影院黑边/中央图标/冲击环/白闪）全在
    // `DuelStageChrome`——它是 MonoBehaviour、自走 Update，所以本类在插值、
    // 在等 WaitForSeconds、在放 flipbook 时，屏上都始终有东西在动。加装饰只动
    // 那一个类，不要回头往编排里塞。
    //
    // 参数一律在 StagePerformanceConfig（Duel* 段），本类不写调参 const。
    // 文档：docs/mechanics/duel.md（前后端总索引）、docs/client/portrait_cutin_assets.md
    // =========================================================================

    public sealed class DuelStage
    {
        /// <summary>飞行立绘的 sorting；背光取 −1（87）垫在其后。
        /// 其余层号（暗幕/屏体/辉光/放射/纹饰/浮尘/图标/冲击环/白闪/黑边）
        /// 归 DuelStageChrome，总表见该类头部。</summary>
        const int OrderPortrait = 88;

        /// <summary>被藏起立绘的卡（飞行体在场期间卡上不能还留着同一张图）。
        /// 无论正常收尾还是 CancelAll 中断，都必须经 <see cref="Restore"/> 还原。</summary>
        readonly List<UnitView> _hidden = new();

        /// <summary>还原所有被藏起的卡面立绘。幂等；中断路径也要调。</summary>
        public void Restore()
        {
            foreach (var unit in _hidden)
                if (unit != null) unit.SetPortraitHidden(false);
            _hidden.Clear();
        }

        // ------------------------------------------------------------ 主流程

        public IEnumerator Run(VFXContext ctx, Transform root,
                               UnitView left, UnitView right,
                               int passes, string winnerId, System.Action onClash)
        {
            var (halfW, halfH) = CutInService.ScreenRect();
            passes = Mathf.Clamp(passes, 1, 3);

            // 华饰与氛围（暗幕/屏体/阵营辉光/放射/浮尘/黑边/图标/冲击环）全在
            // DuelStageChrome：它是 MonoBehaviour，自走 Update，所以本类插值也好、
            // 等 WaitForSeconds 也好，屏上永远有东西在动。
            var chrome = DuelStageChrome.Build(root, halfW, halfH,
                FactionOf(left), FactionOf(right));

            var fL = Fighter.Make(root, left, -1, halfW, halfH);
            var fR = Fighter.Make(root, right, +1, halfW, halfH);
            chrome.OnTick = dt => { fL.TickIdle(dt); fR.TickIdle(dt); };

            var rig = StageCameraRig.Ensure();
            try
            {
                // ⓪ 起势（爆点 1 前半：蓄）
                yield return Anticipate(ctx, fL, fR);

                // ① 放 —— 脚下炸开、白闪、震屏，放完才进下一拍
                yield return Burst(ctx, chrome, left, right, fL, fR);

                // ② 推镜（**独立一拍**）：镜头压近、俯角抬到垂直卡面，卡面显著变大，
                // 到位后定住 DuelCameraHoldSeconds。
                // 与出框并拍时（旧版）观众注意力全在飞出去的人身上，运镜等于白做——
                // 要让人感到"镜头推进来了"，就得给它一拍**只有它在动**。
                yield return PushIn(ctx, rig);

                // ③ 出框：此刻才藏卡面立绘、亮起 cut-in 替身——更早藏会导致
                // 出阵卡面特效阶段卡框空空（替身在 cut-in 根上、战场上看不见）。
                fL.Pose(0f); fR.Pose(0f);
                ConcealCardsForFlyOut(fL, fR);
                float dIn = ctx.Scaled(StagePerformanceConfig.DuelFlySeconds);
                for (float t = 0f; t < dIn; t += Time.deltaTime)
                {
                    float raw = Mathf.Clamp01(t / dIn);
                    // OutBack：冲过头一点再收住＝"被拽出来"而不是"滑出来"
                    float p = OutBack(raw);
                    fL.Pose(p); fR.Pose(p);
                    chrome.SetOpen(raw);
                    yield return null;
                }
                fL.Pose(1f); fR.Pose(1f);
                chrome.SetOpen(1f);

                // ② 亮屏
                float dIcon = ctx.Scaled(StagePerformanceConfig.DuelIconSeconds);
                for (float t = 0f; t < dIcon; t += Time.deltaTime)
                {
                    chrome.SetIcon(OutCubic(t / dIcon));
                    yield return null;
                }
                chrome.SetIcon(1f);

                // ③ 交错 + 播放，共 passes 轮
                for (int i = 0; i < passes; i++)
                {
                    // 爆点 2：末轮之前吸一口气。没有这口气，三轮交错是等速流水账，
                    // 最后一击也就不是"最后一击"。
                    if (i == passes - 1 && passes > 1) yield return Brace(ctx, chrome, fL, fR);
                    yield return Cross(ctx, chrome, fL, fR, onClash);
                    yield return Action(ctx, fL, fR, i, passes);
                }

                // ⑤ 定胜负（爆点 3）
                var (win, lose) = Resolve(fL, fR, winnerId);
                yield return Result(ctx, chrome, win, lose);

                // ⑥ 回框（爆点 4：落框）。镜头**仍在近处**，落框看得清。
                yield return ReturnHome(ctx, chrome, fL, fR);

                // ⑦ 撤镜：镜头先还位，战场回到常规俯视——胜负特效要落在
                // 「看得见整张牌 + 脚下定位圆」的机位上，不能还在近景里炸。
                yield return PullOut(ctx, rig);

                // ⑧ 胜负特效（镜头已还位、卡已落框）：胜者卡面加冕 + 败者定位圆留痕。
                // 放完才算演完。全序：出阵→推镜→出框→交错→回框→撤镜→胜负特效。
                yield return FireResultVfx(ctx, win, lose);
            }
            finally
            {
                rig?.Release();
                Restore();
            }
        }

        /// <summary>出框瞬间：藏卡面立绘 + 亮起 cut-in 替身。蓄力/出阵卡面特效/
        /// 推镜阶段必须留着真立绘，否则卡框空壳只剩粒子。</summary>
        void ConcealCardsForFlyOut(Fighter a, Fighter b)
        {
            foreach (var f in new[] { a, b })
            {
                if (f == null) continue;
                f.SetSubstituteVisible(true);
                if (f.Unit == null) continue;
                f.Unit.SetPortraitHidden(true);
                if (!_hidden.Contains(f.Unit)) _hidden.Add(f.Unit);
            }
        }

        /// <summary>蓄：立绘先往卡里"陷"一点（Pose 走负值 = 反向外插）。
        /// 预备动作是爆发力的唯一来源——直接起飞只会读作位移。
        /// 此拍真卡立绘仍可见；替身尚未亮，Pose 只预热数值。</summary>
        IEnumerator Anticipate(VFXContext ctx, Fighter a, Fighter b)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.DuelAnticipateSeconds);
            float depth = StagePerformanceConfig.DuelAnticipateDepth;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float p = -depth * Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI * 0.5f);
                a.Pose(p); b.Pose(p);
                yield return null;
            }
        }

        /// <summary>放：两名武将脚下（**定位圆**，不是投影圆——这是"站着的那一圈"）
        /// 同时炸开出阵特效，配震屏，**并等它放完**（发射窗口，不含烟尾）。
        ///
        /// 此刻暗幕还没压下来（见 <c>DuelVeilDelay</c>），所以这一炸是在
        /// **真实战场上**看见的，观众才会把"屏"理解成随后被拉进去的地方。
        ///
        /// 等待用真实秒（`WaitForSeconds`）而不是 `ctx.Scaled`：粒子按真实时间播，
        /// 把这段乘倍速只会把特效拦腰截断。上限 `DuelVfxWaitCap` 兜住厂包件的
        /// 不可控时长。</summary>
        IEnumerator Burst(VFXContext ctx, DuelStageChrome chrome, UnitView a, UnitView b,
                          Fighter fa, Fighter fb)
        {
            string groundKey = StagePerformanceConfig.DuelLaunchVfxKey;
            string cardKey = StagePerformanceConfig.DuelLaunchCardVfxKey;
            float cap = StagePerformanceConfig.DuelVfxWaitCap;
            float wait = 0f;
            if (ctx.Vfx != null)
            {
                wait = Mathf.Max(ctx.Vfx.EmitWindow(groundKey, cap),
                                 ctx.Vfx.EmitWindow(cardKey, cap));
            }
            // 探不到（key 未落盘 / 全是循环层 / 无粒子）时给个保底节拍，否则这一拍
            // 直接消失、蓄力白蓄——**不能因为素材缺失就把节奏也丢了**。
            float play = Mathf.Max(wait, StagePerformanceConfig.DuelVfxFallbackSeconds);
            float life = play + StagePerformanceConfig.DuelVfxTailSeconds;

            var live = new List<GameObject>(4);
            foreach (var unit in new[] { a, b })
            {
                if (unit == null || ctx.Vfx == null) continue;
                // 地面：定位圆 Effect28
                var g = ctx.Vfx.PlayAt(groundKey,
                    ArenaSlotLayout.AnchorCircleCenter(unit.RestPosition), life);
                if (g != null) live.Add(g);
                // 卡面追加：画廊 1/8 件 8/60
                var c = ctx.Vfx.PlayOn(cardKey, unit.transform, life);
                if (c != null) live.Add(c);
            }
            ctx.Shake(StagePerformanceConfig.DuelLaunchShake, 0.3f);
            chrome.Pulse();

            // 这一拍**不能用 WaitForSeconds 干等**：脚下虽然在炸，两张立绘却纹丝不动，
            // 1.5 秒的"人物静止"会把爆发读成背景动画。让他们在原地继续下沉、发抖，
            // 力量憋在身上——观众才会预期"憋到头就会被弹出去"。
            // 零死帧原则的时间版：**任何一拍都必须有主体在动，不只是屏上有东西在动**。
            float depth = StagePerformanceConfig.DuelAnticipateDepth;
            float hz = StagePerformanceConfig.DuelCoilTrembleHz;
            float amp = StagePerformanceConfig.DuelCoilTrembleAmp;
            for (float t = 0f; t < play; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / play);
                // 越憋越深、抖幅越大
                float pose = -depth * (1f + k)
                             + Mathf.Sin(t * hz * Mathf.PI * 2f) * amp * k;
                fa.Pose(pose); fb.Pose(pose);
                yield return null;
            }

            // 交拍前**收势**：出阵件里有循环层（火环/扭曲环），它们不会自己停，
            // 任由其全速发射到回收那一刻，人飞出去时地上还在猛烧，读作
            // "炸到一半被打断"。此处只掐新粒子、留余烬，下一拍就成了
            // "在余烬中被拽走"——顺序感靠的是收势，不是把等待拉到无限长。
            foreach (var go in live) VFXManager.StopEmitting(go);
        }

        /// <summary>推镜：俯角抬到与卡面垂直、距离显著拉近。</summary>
        static void PushCamera(StageCameraRig rig, float p) =>
            rig?.Blend(StagePerformanceConfig.DuelCameraPitchDeg,
                       StagePerformanceConfig.DuelCameraDistance, OutCubic(p));

        /// <summary>② 推镜一拍：压到位再**定住** <c>DuelCameraHoldSeconds</c>。
        ///
        /// 定住那一下是这拍的全部意义——运动结束时的静止才让人确认"镜头到位了、
        /// 卡面变大了"。一路推到底就接下一拍，观众只感到画面晃了晃。
        /// 定住期间屏上并不空：卡牌自身的待机浮动、脚下的出阵余烬都还在。</summary>
        IEnumerator PushIn(VFXContext ctx, StageCameraRig rig)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.DuelCameraPushSeconds);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                PushCamera(rig, Mathf.Clamp01(t / dur));
                yield return null;
            }
            PushCamera(rig, 1f);
            yield return new WaitForSeconds(StagePerformanceConfig.DuelCameraHoldSeconds);
        }

        /// <summary>⑦ 撤镜一拍：还原到 CameraFitter 的常规机位。
        /// 排在回框之后、胜负特效之前——近景只服务出框/回框，胜负留痕要在常视机位播。</summary>
        IEnumerator PullOut(VFXContext ctx, StageCameraRig rig)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.DuelCameraPushSeconds);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                PushCamera(rig, 1f - Mathf.Clamp01(t / dur));
                yield return null;
            }
            PushCamera(rig, 0f);
        }

        /// <summary>末轮前的静滞：两人向后拉开、图标缩紧。屏上仍有 Chrome 在动，
        /// 不违背零死帧；这里要的是"运动量骤降"，不是真静止。</summary>
        IEnumerator Brace(VFXContext ctx, DuelStageChrome chrome, Fighter a, Fighter b)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.DuelBraceSeconds);
            float back = a.SlotPos.magnitude * StagePerformanceConfig.DuelBraceBack;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float p = OutCubic(t / dur);
                a.SetSlotOffset(new Vector3(-back * p, 0f, 0f));
                b.SetSlotOffset(new Vector3(back * p, 0f, 0f));
                chrome.SetIcon(1f - 0.35f * p);
                yield return null;
            }
        }

        /// <summary>⑥ 回框：暗幕**提前**散（<c>DuelVeilDelay</c> 的镜像），
        /// 立绘沿原路飞回卡框。**镜头此刻仍在近处**，落框这一下看得清；
        /// 下一拍才撤镜（⑦ <see cref="PullOut"/>），再播胜负特效。</summary>
        IEnumerator ReturnHome(VFXContext ctx, DuelStageChrome chrome, Fighter a, Fighter b)
        {
            float dur = ctx.Scaled(StagePerformanceConfig.DuelFlySeconds);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float raw = Mathf.Clamp01(t / dur);
                float p = InCubic(raw);
                // 胜负染色同步褪回本色：卡框里的立绘永远是本色，
                // 带着胜/败色飞回去、落地再弹回白，会看见一次突跳。
                a.FadeTintToNormal(raw); b.FadeTintToNormal(raw);
                a.Pose(1f - p); b.Pose(1f - p);
                chrome.SetOpen(1f - raw);
                chrome.SetIcon(1f - raw);
                yield return null;
            }
            a.Pose(0f); b.Pose(0f);

            // 立绘已回到卡框：**先收替身、再还原卡面立绘**，顺序反了会有一帧两张图
            // 叠在一起（替身与真立绘此刻位置完全重合，读作重影）。
            a.Hide(); b.Hide();
            Restore();
        }

        /// <summary>胜者卡面加冕、败者定位圆地面留痕 + 败者卡面追加，**等都放完**。
        /// key 在 StagePerformanceConfig；等待规则同 <see cref="Burst"/>（真实秒 + 上限）。</summary>
        IEnumerator FireResultVfx(VFXContext ctx, Fighter win, Fighter lose)
        {
            if (ctx.Vfx == null || win == null || lose == null) yield break;

            float cap = StagePerformanceConfig.DuelVfxWaitCap;
            float tail = StagePerformanceConfig.DuelVfxTailSeconds;
            float play = Mathf.Max(
                StagePerformanceConfig.DuelVfxFallbackSeconds,
                Mathf.Max(
                    ctx.Vfx.EmitWindow(StagePerformanceConfig.DuelWinnerVfxKey, cap),
                    Mathf.Max(
                        ctx.Vfx.EmitWindow(StagePerformanceConfig.DuelLoserVfxKey, cap),
                        ctx.Vfx.EmitWindow(StagePerformanceConfig.DuelLoserCardVfxKey, cap))));
            float life = play + tail;

            if (win.Unit != null)
                ctx.Vfx.PlayOn(StagePerformanceConfig.DuelWinnerVfxKey, win.Unit.transform, life);

            if (lose.Unit != null)
            {
                // 原版：定位圆地面 + 自研裂地（Effect8 贴花 URP 画不出，P-33）
                ctx.Vfx.PlayAt(StagePerformanceConfig.DuelLoserVfxKey,
                    ArenaSlotLayout.AnchorCircleCenter(lose.Unit.RestPosition), life);
                if (ArenaSlotLayout.GroundActive)
                    GroundCrackService.PlayHit(ctx, null, lose.Unit);
                // 追加：画廊 1/8 件 32/60 观感挂败者卡面
                ctx.Vfx.PlayOn(StagePerformanceConfig.DuelLoserCardVfxKey,
                    lose.Unit.transform, life);
            }

            yield return new WaitForSeconds(play);
        }

        static Color FactionOf(UnitView unit) =>
            unit != null ? BattleBoardView.FactionColorOf(unit.Hero.TemplateId) : Color.gray;

        // ------------------------------------------------------------ 分幕

        /// <summary>交错：两人对穿到对侧再弹回本位。一上弧一下弧，否则两张立绘
        /// 在中点完全重叠，观众只看到"闪了一下"，读不出错身。</summary>
        IEnumerator Cross(VFXContext ctx, DuelStageChrome chrome, Fighter a, Fighter b,
                          System.Action onClash)
        {
            float dOut = ctx.Scaled(StagePerformanceConfig.DuelCrossSeconds);
            float arc = a.SlotPos.magnitude * StagePerformanceConfig.DuelCrossArc;
            // 起点取**当前**偏移而不是零：末轮前的 Brace 把两人往后拉开了，
            // 从零起算会在起跳第一帧把这段后撤瞬间抹掉，读作跳帧。
            Vector3 fromA0 = a.SlotOffset, fromB0 = b.SlotOffset;
            bool clashed = false;
            for (float t = 0f; t < dOut; t += Time.deltaTime)
            {
                float p = Mathf.Clamp01(t / dOut); // 线性＝高速掠过
                float bow = Mathf.Sin(p * Mathf.PI) * arc;
                a.SetSlotOffset(Vector3.LerpUnclamped(fromA0, b.SlotPos - a.SlotPos, p)
                                + new Vector3(0f, bow, 0f));
                b.SetSlotOffset(Vector3.LerpUnclamped(fromB0, a.SlotPos - b.SlotPos, p)
                                + new Vector3(0f, -bow, 0f));
                if (!clashed && p >= 0.5f)
                {
                    clashed = true;
                    onClash?.Invoke();
                    chrome.Pulse();
                    chrome.SetIcon(1f); // 解除 Brace 时收紧的图标：气在这一刻放掉
                }
                yield return null;
            }

            float dBack = ctx.Scaled(StagePerformanceConfig.DuelCrossSeconds
                                     * StagePerformanceConfig.DuelCrossReturnRatio);
            Vector3 fromA = a.SlotOffset, fromB = b.SlotOffset;
            for (float t = 0f; t < dBack; t += Time.deltaTime)
            {
                float p = OutCubic(t / dBack);
                a.SetSlotOffset(Vector3.LerpUnclamped(fromA, Vector3.zero, p));
                b.SetSlotOffset(Vector3.LerpUnclamped(fromB, Vector3.zero, p));
                yield return null;
            }
            a.SetSlotOffset(Vector3.zero);
            b.SetSlotOffset(Vector3.zero);
        }

        /// <summary>播放本轮动作：攻方 strike、守方 react，末轮双方同时 strike。
        /// 无 flipbook 资源时序列只有 1 帧（静态立绘），于是本段就是"图片占满时长"。</summary>
        IEnumerator Action(VFXContext ctx, Fighter a, Fighter b, int round, int passes)
        {
            bool last = round == passes - 1;
            bool aStrikes = round % 2 == 0;

            a.BeginClip(last || aStrikes);
            b.BeginClip(last || !aStrikes);

            float dur = ctx.Scaled(StagePerformanceConfig.DuelActionSeconds
                                   * (last ? StagePerformanceConfig.DuelFinalRoundScale : 1f));
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float p = Mathf.Clamp01(t / dur);
                a.TickClip(p);
                b.TickClip(p);
                yield return null;
            }
            a.EndClip();
            b.EndClip();
        }

        /// <summary>胜者由 `duel_result.winner_id` 下发，客户端**只读不判**（零结算红线）。
        /// 认不出（平局/字段缺失）则两边都返回 null，走并列收势。</summary>
        static (Fighter win, Fighter lose) Resolve(Fighter a, Fighter b, string winnerId)
        {
            if (string.IsNullOrEmpty(winnerId)) return (null, null);
            if (a.Unit != null && a.Unit.Hero.HeroId == winnerId) return (a, b);
            if (b.Unit != null && b.Unit.Hero.HeroId == winnerId) return (b, a);
            return (null, null);
        }

        /// <summary>定胜负：胜者提亮上前，败者压暗后仰。无胜者则整段跳过。</summary>
        IEnumerator Result(VFXContext ctx, DuelStageChrome chrome, Fighter win, Fighter lose)
        {
            if (win == null || lose == null) yield break;

            chrome.Pulse();
            float dur = ctx.Scaled(StagePerformanceConfig.DuelResultSeconds);
            float lift = win.SlotScale.x * StagePerformanceConfig.DuelWinnerLift;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float p = OutCubic(t / dur);
                win.SetSlotOffset(new Vector3(0f, lift * p, 0f));
                win.SetScaleMul(Mathf.LerpUnclamped(1f, StagePerformanceConfig.DuelWinnerScale, p));
                win.SetTint(Color.Lerp(Color.white, WinTint, p));
                lose.SetSlotOffset(new Vector3(0f, -lift * 0.6f * p, 0f));
                lose.SetScaleMul(Mathf.LerpUnclamped(1f, StagePerformanceConfig.DuelLoserScale, p));
                lose.SetTint(Color.Lerp(Color.white, LoseTint, p));
                yield return null;
            }
            yield return new WaitForSeconds(ctx.Scaled(StagePerformanceConfig.DuelResultHoldSeconds));
        }

        // ------------------------------------------------------------ 飞行体

        /// <summary>一名参战武将在展示屏上的替身：持有「卡上姿态」与「槽位姿态」
        /// 两端，<see cref="Pose"/>(0..1) 在两端之间插值——出框与回框共用同一条路径，
        /// 所以回去一定落回原处，不会因为两段各写一套坐标而错位。</summary>
        sealed class Fighter
        {
            public UnitView Unit;
            public Vector3 SlotPos;
            public Vector3 SlotScale;

            Transform _tr, _root;
            SpriteRenderer _sr, _glow, _src;
            Vector3 _cardPos, _cardScale;
            Quaternion _cardRot;
            Vector3 _slotOffset;
            float _scaleMul = 1f;
            Color _tint = Color.white, _glowColor;
            Sprite _idle;
            Sprite[] _strike, _react, _clip;
            float _p, _breathTime, _breathPhase;

            public Vector3 SlotOffset => _slotOffset;

            public static Fighter Make(Transform root, UnitView unit, int side,
                                       float halfW, float halfH)
            {
                var f = new Fighter { Unit = unit };
                var src = unit != null ? unit.PortraitRenderer : null;
                string templateId = unit != null ? unit.Hero.TemplateId : null;
                Color faction = templateId != null
                    ? BattleBoardView.FactionColorOf(templateId)
                    : Color.gray;

                f._idle = src != null && src.sprite != null
                    ? src.sprite
                    : PlaceholderFactory.GetSprite("Portraits", templateId ?? "unknown",
                        Color.Lerp(faction, Color.black, 0.35f), 96);
                f._strike = LoadFrames(templateId, "strike", f._idle);
                f._react = LoadFrames(templateId, "react", f._idle);

                var go = new GameObject($"duel_fighter_{templateId}");
                go.transform.SetParent(root, false);
                f._tr = go.transform;
                f._sr = go.AddComponent<SpriteRenderer>();
                f._sr.sprite = f._idle;
                f._sr.sortingOrder = OrderPortrait;

                // 背光：同一张立绘放大一圈垫在身后、染阵营色。没有 shader 也能
                // 做出描边发光，把主体从背景里拔出来——不然再华丽的底也只是
                // 「立绘贴在图上」。它跟着 _sr 换帧（见 TickClip）。
                var glowGo = new GameObject("glow");
                glowGo.transform.SetParent(go.transform, false);
                glowGo.transform.localScale =
                    Vector3.one * StagePerformanceConfig.DuelPortraitGlowScale;
                f._glow = glowGo.AddComponent<SpriteRenderer>();
                f._glow.sprite = f._idle;
                f._glow.sortingOrder = OrderPortrait - 1;
                f._glowColor = Color.Lerp(faction, Color.white, 0.35f);
                f._glow.color = Fade(f._glowColor, 0f);

                // 两人错开呼吸相位，否则同起同落会读成"两张图一起在抖"
                f._breathPhase = side > 0 ? Mathf.PI : 0f;

                // 起点＝卡面立绘的世界姿态，换算到 cut-in 平面局部空间。
                // **每帧重算**（SyncCardPose），不缓存：cut-in 挂点是相机的子物体，
                // 而单挑期间相机会被 StageCameraRig 推近抬角——缓存一次的话，
                // 相机一动这个"卡上那一端"就跟真卡对不上，回框会落偏。
                //
                // 【何时藏真立绘】不在 Make 时藏——出阵卡面特效（Burst）与推镜
                // 阶段观众看的是真卡；替身先熄灭，出框瞬间再 ConcealCardsForFlyOut
                // 切换。更早藏＝卡框空壳 + 粒子，读作「立绘没了」（P-69）。
                f._root = root;
                f._src = src;
                if (src != null)
                    f.SyncCardPose();
                else
                {
                    f._cardPos = new Vector3(side * halfW * 0.5f, -halfH * 0.6f, 0f);
                    f._cardRot = Quaternion.identity;
                    f._cardScale = Vector3.one * 0.1f;
                }

                f.SlotPos = new Vector3(side * halfW * StagePerformanceConfig.DuelSlotX,
                    halfH * StagePerformanceConfig.DuelSlotY, 0f);
                f.SlotScale = ContainScale(f._idle,
                    halfW * StagePerformanceConfig.DuelSlotWidth,
                    halfH * StagePerformanceConfig.DuelSlotHeight);

                f.Pose(0f);
                f.SetSubstituteVisible(false); // 出框前不抢真立绘的戏
                return f;
            }

            /// <summary>cut-in 替身显隐（真卡立绘由外部 SetPortraitHidden 管）。</summary>
            public void SetSubstituteVisible(bool on)
            {
                if (_sr != null) _sr.enabled = on;
                if (_glow != null) _glow.enabled = on;
            }

            /// <summary>p=0 贴在卡上，p=1 站在槽位。待机呼吸（<see cref="TickIdle"/>）
            /// 叠在槽位端，所以卡上那一端不受影响、出框起点始终严丝合缝。</summary>
            public void Pose(float p)
            {
                if (_tr == null) return;
                _p = p;
                SyncCardPose();
                float breath = Mathf.Sin(
                    _breathTime * StagePerformanceConfig.DuelPortraitBreathHz * Mathf.PI * 2f
                    + _breathPhase);
                Vector3 slot = SlotPos + _slotOffset
                    + new Vector3(0f, breath * SlotScale.y
                        * StagePerformanceConfig.DuelPortraitBreathAmp, 0f);
                float pulse = 1f + breath * StagePerformanceConfig.DuelPortraitBreathAmp * 0.5f;

                _tr.localPosition = Vector3.LerpUnclamped(_cardPos, slot, p);
                _tr.localRotation = Quaternion.SlerpUnclamped(_cardRot, Quaternion.identity, p);
                _tr.localScale = Vector3.LerpUnclamped(_cardScale, SlotScale * _scaleMul * pulse, p);

                if (_glow != null)
                    _glow.color = Fade(_glowColor,
                        StagePerformanceConfig.DuelPortraitGlowAlpha * Mathf.Clamp01(p)
                        * (0.75f + 0.25f * breath));
            }

            /// <summary>把「卡上那一端」刷成真卡此刻的姿态（换算到 cut-in 平面局部空间）。
            /// 挂点随相机走、卡在世界里不动，所以这两个空间的关系每帧都在变。</summary>
            void SyncCardPose()
            {
                if (_src == null || _root == null) return;
                var t = _src.transform;
                _cardPos = _root.InverseTransformPoint(t.position);
                _cardRot = Quaternion.Inverse(_root.rotation) * t.rotation;
                _cardScale = t.lossyScale;
            }

            /// <summary>由 DuelStageChrome 的自走时钟每帧驱动：推进呼吸相位并重绘。
            /// 交错与动作之间若完全静止就是死帧（R-4.1），这一条兜住。</summary>
            public void TickIdle(float dt)
            {
                _breathTime += dt;
                Pose(_p);
            }

            /// <summary>收掉替身（连同背光）。回框落定后调，之后不要再 Pose。</summary>
            public void Hide()
            {
                if (_tr != null) _tr.gameObject.SetActive(false);
            }

            public void SetSlotOffset(Vector3 offset) { _slotOffset = offset; Pose(1f); }
            public void SetScaleMul(float mul) { _scaleMul = mul; Pose(1f); }

            public void SetTint(Color c)
            {
                _tint = c;
                if (_sr != null) _sr.color = c;
            }

            /// <summary>把当前染色按 p（0→1）褪回白。</summary>
            public void FadeTintToNormal(float p)
            {
                if (_sr != null) _sr.color = Color.Lerp(_tint, Color.white, Mathf.Clamp01(p));
            }

            public void BeginClip(bool strike) => _clip = strike ? _strike : _react;

            public void TickClip(float p)
            {
                if (_sr == null || _clip == null || _clip.Length == 0) return;
                int i = Mathf.Clamp(Mathf.FloorToInt(p * _clip.Length), 0, _clip.Length - 1);
                SetSprite(_clip[i]);
            }

            public void EndClip()
            {
                _clip = null;
                SetSprite(_idle);
            }

            /// <summary>背光必须跟着换同一帧，否则动作里会露出上一帧的轮廓。</summary>
            void SetSprite(Sprite sprite)
            {
                if (_sr != null) _sr.sprite = sprite;
                if (_glow != null) _glow.sprite = sprite;
            }

            /// <summary>逐帧序列加载：{template_id}_{clip}_00 起连号，断号即停。
            /// 一帧都没有 → 返回静态立绘单帧（这一段就变成"图片占满时长"）。</summary>
            static Sprite[] LoadFrames(string templateId, string clip, Sprite fallback)
            {
                var frames = new List<Sprite>();
                if (!string.IsNullOrEmpty(templateId))
                    for (int i = 0; i < 64; i++)
                    {
                        var s = PlaceholderFactory.TryLoadSprite(
                            "DuelAction", $"{templateId}_{clip}_{i:00}");
                        if (s == null) break;
                        frames.Add(s);
                    }
                if (frames.Count == 0)
                    return fallback != null ? new[] { fallback } : System.Array.Empty<Sprite>();
                return frames.ToArray();
            }
        }

        // ------------------------------------------------------------ 构件与缓动

        static readonly Color WinTint = new(1f, 0.98f, 0.86f);
        static readonly Color LoseTint = new(0.42f, 0.44f, 0.5f);

        static Vector3 ContainScale(Sprite sprite, float slotW, float slotH)
        {
            if (sprite == null) return Vector3.one;
            var size = sprite.bounds.size;
            if (size.x <= 1e-4f || size.y <= 1e-4f) return Vector3.one;
            return Vector3.one * Mathf.Min(slotW / size.x, slotH / size.y);
        }

        static Color Fade(Color c, float a) => new(c.r, c.g, c.b, a);

        static float OutCubic(float p) => 1f - Mathf.Pow(1f - Mathf.Clamp01(p), 3f);
        static float InCubic(float p) => Mathf.Pow(Mathf.Clamp01(p), 3f);

        /// <summary>过冲后收住。终值精确为 1（标准 OutBack 公式），
        /// 所以出框结束时立绘一定停在槽位上，不会差一点点。</summary>
        static float OutBack(float p)
        {
            const float s = 1.35f;
            float x = Mathf.Clamp01(p) - 1f;
            return 1f + x * x * ((s + 1f) * x + s);
        }
    }
}
