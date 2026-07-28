namespace ClientBattle.Units
{
    // =========================================================================
    // 客户端舞台演出【静态配置】——改数字即调参，重新进 Play 生效。
    //
    // 收口范围：卡牌在舞台上「怎么动」的全部可调量（卡姿抖动 / 微调圆 /
    // 受击击退与抖动 / 出手三拍 / 突进残影 / 接地阴影）。
    // 新增任何演出手感参数一律加到这里，**禁止再在各表现类里散落 const**。
    //
    // 与 BattlefieldLayoutConfig 的分工：
    //   BattlefieldLayoutConfig = 舞台**几何**（分区、卡尺、站位微抖半径、浮空高度）
    //   StagePerformanceConfig  = 舞台**演出与机位观感**（俯角 / 卡姿抖动 / 微调圆 /
    //                             击退 / 三拍 / 残影 / 接地阴影）
    // 卡牌后倾**基准角**仍在 CameraFitter.CardPitchDeg（站位/影子/定位圆几何真源）；
    // 这里放相机俯角与每卡随机偏移量。
    //
    // 文档：docs/client/performance_mechanisms.md 、 docs/client/arena_stage.md
    // =========================================================================

    public static class StagePerformanceConfig
    {
        // ------------------------------------------------------------ 机位

        /// <summary>相机俯角（度）＝ Euler X ＝ 从水平往下压的度数。越大越俯视。
        /// 与卡后倾 <c>CameraFitter.CardPitchDeg</c>（45°）解耦——只调机位、不动
        /// 卡几何。现行 **35**。</summary>
        public static float PilotPitchDeg = 36f;

        // ------------------------------------------------------------ 卡姿抖动

        /// <summary>每卡后倾角在 **基准角 ± 此值** 内随机（度）。
        /// 基准角＝`CameraFitter.CardPitchDeg`（现 45°，几何角度链唯一真源），
        /// 故默认 5 ＝ 实际 **40°~50°**。让整排卡不像同一块板刷出来的。
        ///
        /// **只抖视觉**：站位落点与影子几何一律仍按基准角算，故此值不宜超过 ~8°，
        /// 再大卡脚与影子就会看出偏移。</summary>
        public static float CardPitchJitterDeg = 5f;

        // ------------------------------------------------------------ 微调圆（旧名"击打圆"）

        /// <summary>微调圆半径 = 站位微抖圆半径（`StanceLayout.SlotJitterRadius`）× 此值。
        /// 1 ＝ 与原微抖圆完全重合。**受击击退与出击后的前进休息点都被夹在这个圆内**，
        /// 所以卡牌只会在自己的圆盘里游走，永远不会越打越偏。</summary>
        public static float TuneCircleScale = 1f;

        // ------------------------------------------------------------ 受击击退（位移）

        /// <summary>击退距离的随机区间，单位＝**微调圆半径的倍数**（0~1）。
        /// 每次受击在区间内随机一个距离，把**定位点**沿受击线后退到那里；
        /// 推开与落定两段位移都钉在这条线上，越圆即截断到圆边。
        ///
        /// 用半径倍数而不是世界单位：这样距离天然随卡尺缩放，且 ≤1 就等于
        /// 「永远不会顶出微调圆」，不必依赖裁剪兜底也不会出现离谱的位移。</summary>
        public static float KnockbackNormalMin = 0.35f;
        public static float KnockbackNormalMax = 0.70f;
        public static float KnockbackCritMin = 0.70f;
        public static float KnockbackCritMax = 1.00f;

        /// <summary>推开点的过冲倍数（≥1，仍在受击线上、仍被微调圆截断）：
        /// 先冲过头一点再落回，才有被撞的弹性；1 ＝ 直接滑到落点，偏"推"不"撞"。</summary>
        public static float KnockOvershoot = 1.25f;

        /// <summary>被推开的时长（秒）。要短，才有被撞飞的突然感。</summary>
        public static float KnockOutSecondsCrit = 0.09f;
        public static float KnockOutSecondsNormal = 0.07f;

        /// <summary>弹回落点的时长（秒）。要比推开长，才是「站稳」不是「弹簧」。</summary>
        public static float KnockBackSecondsCrit = 0.26f;
        public static float KnockBackSecondsNormal = 0.20f;

        // ------------------------------------------------------------ 受击抖动（沿线前后颤）

        // 【安卓帧率定标】独立版/真机均 vSync 锁屏刷（多为 60），但战斗负载下
        // 中端机常见掉到 30~45 fps——颤动频率必须按 **30 fps 下限** 定，
        // 不能按编辑器满帧 60 定。每周期帧数 = fps / Hz：
        //   18 Hz @ 30fps ≈ 1.7 帧 → 采成噪点，再大振幅也读不出「震」；
        //   10 Hz @ 30fps ≈ 3 帧 / @ 45 ≈ 4.5 / @ 60 ≈ 6 → 各档都能画出摆动。
        // 持续低到 ~5 Hz 才读成「晃悠」；短促 0.3 s 内的 10 Hz 仍是「被震到」。

        /// <summary>受击颤动峰值振幅，× 微调圆半径。颤动＝击退**落定后**围绕落点
        /// 沿同一条受击线的前后颤，纯动画、结束回到落点，不改定位点。
        /// （旋转式抖动已废：近正面卡的面内自旋不改轮廓、俯仰被投影吃掉，读不出来。）
        /// 2026-07-28：实测「只看得见击退、看不出震」，振幅**翻倍**。</summary>
        public static float HitTrembleAmpCrit = 0.44f;
        public static float HitTrembleAmpNormal = 0.26f;

        /// <summary>受击颤动时长（秒）。从击退落定那一刻起算。</summary>
        public static float HitTrembleSecondsCrit = 0.36f;
        public static float HitTrembleSecondsNormal = 0.28f;

        /// <summary>受击颤动频率（Hz）。按安卓战斗负载 **30 fps 下限** 定：
        /// 10 Hz ≈ 3 帧一周期。编辑器满帧上看起来会稍「慢」，以真机为准。</summary>
        public static float HitTrembleFrequency = 10f;

        /// <summary>受击颤动的衰减指数（作用在剩余时间比 k 上）。
        /// 1.0＝线性；越大起手越猛、尾巴越快塌。1.1 让中段仍看得见。</summary>
        public static float HitTrembleDecayPower = 1.1f;

        // ------------------------------------------------------------ 出击后的前进休息点

        /// <summary>出击收势时沿行动方向前进的距离，× 微调圆半径的随机区间。
        /// 打出去的人会往前站一点，受击的人被推回去一点——一来一回，
        /// 整局下来站位是活的而不是钉死的。</summary>
        public static float AdvanceRestForwardMin = 0.35f;
        public static float AdvanceRestForwardMax = 0.90f;

        /// <summary>前进落点的横向随机幅度，× 微调圆半径（±此值）。</summary>
        public static float AdvanceRestLateral = 0.35f;

        // ------------------------------------------------------------ 出手三拍

        /// <summary>预备（反向蓄力）时长（秒）。</summary>
        public static float WindupSeconds = 0.12f;

        /// <summary>发力（加速突进）时长（秒）。</summary>
        public static float StrikeSeconds = 0.16f;

        /// <summary>收势（过冲回位）时长（秒）。</summary>
        public static float RecoverSeconds = 0.26f;

        /// <summary>蓄力距离 = 突进距离 × 此值，并被 <see cref="WindupMax"/> 封顶。
        /// 按比例是为了近距离补刀不会蓄出夸张的后仰。</summary>
        public static float WindupRatio = 0.16f;
        public static float WindupMax = 0.30f;

        // ------------------------------------------------------------ 突进残影

        /// <summary>残影采样间隔与单张寿命（常速秒，运行时经 ctx.Scaled 换算）。</summary>
        public static float GhostInterval = 0.035f;
        public static float GhostLife = 0.22f;

        /// <summary>残影初始透明度：太实糊成一团，太虚等于没有。</summary>
        public static float GhostAlpha = 0.42f;

        /// <summary>残影初始收缩比例（尾端更小，一串下来是收敛的锥形）。</summary>
        public static float GhostShrink = 0.06f;

        // ------------------------------------------------------------ 接地阴影

        /// <summary>影子宽度 / 卡宽。略窄于卡面：脚下的接触面比卡面窄。</summary>
        public static float ShadowWidthRatio = 0.82f;

        /// <summary>影子纵深 / 卡牌地面足迹纵深（`ArenaSlotLayout.CardShadowDepth`）。</summary>
        public static float ShadowDepthRatio = 0.90f;

        public static float ShadowAlpha = 0.46f;

        /// <summary>抬到「半个卡高」时影子缩到此比例、淡到此比例。</summary>
        public static float ShadowLiftMinScale = 0.72f;
        public static float ShadowLiftMinAlpha = 0.42f;

        /// <summary>阵亡后影子淡出时长（秒）。</summary>
        public static float ShadowDefeatFadeSeconds = 0.5f;

        // ------------------------------------------------------------ 单挑舞台 cut-in
        //
        // 全部时长都会经 ctx.Scaled 换算（吃倍速与 DurationMul）。
        // 几何量一律写成**屏幕半宽/半高的倍数**，不写世界单位——cut-in 平面尺寸
        // 随机型宽高比变化，写死世界单位在长屏手机上必然出框。
        // 演出分幕见 VFX/DuelStage.cs 类头，产品说明见 docs/mechanics/duel.md。

        /// <summary>立绘出框飞入 / 飞回卡框的单程时长（秒）。回程复用同一值，
        /// 走的是同一条插值路径的反向，所以一定落回原位。</summary>
        public static float DuelFlySeconds = 0.46f;

        // ---------------------------------------------- 情感爆点：蓄 → 放 → 屏
        //
        // 【为什么要有"蓄"】动作的爆发力 100% 来自预备动作。立绘直接从卡里滑出来，
        // 无论多快都只读作"位移"；先往卡里陷一下再炸出去，才读作"被拽出来"。
        // 这三个参数是同一个爆点的三段，**要一起调**：陷得深就要炸得响。

        /// <summary>出框前的蓄力时长（秒）与陷入深度（Pose 负向外插量，
        /// 0.08＝往卡里再陷 8% 的行程）。深度别超 0.15，会穿到卡背后。</summary>
        public static float DuelAnticipateSeconds = 0.16f;
        public static float DuelAnticipateDepth = 0.08f;

        /// <summary>出阵爆发的震屏强度。</summary>
        public static float DuelLaunchShake = 0.34f;

        /// <summary>暗幕**延迟压下**的比例：出框进度走到此值前暗幕保持全透。
        ///
        /// 【为什么必须延迟】出阵特效（<see cref="DuelLaunchVfxKey"/>）炸在**世界里的
        /// 定位圆**上，而暗幕是 sorting 80 的全屏黑片——不延迟的话这一炸从第一帧
        /// 起就被盖住，等于白播。延迟后观众先在真战场上看见两团爆发，
        /// 世界才暗下去、屏才接管。回程用同一比例的镜像（暗幕提前散），
        /// 好让胜负特效同样落在看得见的战场上。</summary>
        public static float DuelVeilDelay = 0.35f;

        /// <summary>末轮前"静滞"的时长（秒）与两人后撤距离（×槽位到屏心的距离）。
        /// 运动量骤降制造预期；屏上仍有 Chrome 在动，不是死帧。</summary>
        public static float DuelBraceSeconds = 0.22f;
        public static float DuelBraceBack = 0.16f;

        /// <summary>出阵特效播放期间，立绘原地「憋力发抖」的频率（Hz）与幅度。
        /// 这一拍长约 1.5 s，若人物纹丝不动，脚下的爆发会被读成背景动画
        /// （零死帧的时间版：每一拍都要有**主体**在动）。</summary>
        public static float DuelCoilTrembleHz = 11f;
        public static float DuelCoilTrembleAmp = 0.035f;

        // ---------------------------------------------- 单挑推镜（StageCameraRig）

        /// <summary>出框期间相机抬到的俯角（度）。**等于
        /// <see cref="VFX.CameraFitter.CardPitchDeg"/> 时光轴恰好垂直卡面**，
        /// 卡面不再被斜切，是"正脸看着你"的机位。改卡后倾角时这里要跟着改。</summary>
        public static float DuelCameraPitchDeg = 45f;

        /// <summary>推近后的机位距离（常规 <c>CameraFitter.PilotDistance</c>=55）。
        /// 只缩距离、**不动 FOV**：极长焦下距离一缩主体直接顶上来，
        /// 而透视关系不变（改 FOV 会突然变广角脸）。
        ///
        /// 38 ≈ **卡面放大到 1.45 倍**。推镜是独立一拍 + 定格，这点放大量读得出来；
        /// 曾用 28（1.96×）把全阵容卡面裁出画面，硬约束是**六张牌仍在框内**。</summary>
        public static float DuelCameraDistance = 42f;

        /// <summary>推镜/撤镜各自的时长。运镜是**独立一拍**，不再与出框并拍：
        /// 并拍时注意力全在飞出去的人身上，镜头等于白推。</summary>
        public static float DuelCameraPushSeconds = 0.42f;

        /// <summary>推到位后的定格时长。运动结束时的**静止**才让人确认
        /// 「到位了、卡面变大了」；一推到底就接下一拍只会读作画面晃了晃。</summary>
        public static float DuelCameraHoldSeconds = 0.3f;

        // ------------------------------------------- 通用 cut-in 取景（CutInStage）
        //
        // 一切 cut-in 横幅（满档 / 巨伤 / 追击计数）共用同一编排：
        //   推镜 → 横幅 → 本组出手命中 → 撤镜（独占播放单元）。
        // 与单挑同构，只是不飞立绘。比单挑推得**略浅**：这一拍之后紧接的是
        // 出手与命中，机位要留得下突进位移、弹道与裂地，不能只剩一张脸。

        /// <summary>cut-in 期间抬到的俯角（度）。与单挑同向但保守些。</summary>
        public static float CutInCameraPitchDeg = 42f;

        /// <summary>cut-in 推近后的机位距离（常规 55）。46 ≈ 卡面放大 1.2 倍：
        /// 读得出"压过来了"，同时整场站位与弹道全程仍在框内。</summary>
        public static float CutInCameraDistance = 46f;

        /// <summary>推镜/撤镜各自时长。</summary>
        public static float CutInCameraPushSeconds = 0.3f;

        /// <summary>推到位后、切横幅前的定格。横幅本身就是一次强停顿，
        /// 这里只需极短一停给运镜收尾。</summary>
        public static float CutInCameraHoldSeconds = 0.08f;

        // ---------------------------------------------- 罩身错相位（shroud_*）
        //
        // 同一件挂在多人身上时，几份实例是同一帧创建的，会逐帧同步地闪，
        // 读作"一个动画被复制了几份"。挂载时快进一段随机时间 + 给一点速度失谐，
        // 各自就有了自己的节奏（与卡面呼吸的互质频率失谐同源）。

        /// <summary>挂载时随机快进的上限（秒）。要盖过件里最长的循环周期才能真正错开；
        /// 太大则"挂上就已经播了半天"，循环件无所谓、一次性层会被跳过。</summary>
        public static float ShroudDesyncSeconds = 1.6f;

        /// <summary>播放速度失谐（±比例）。只有预演没有失谐时，相位差是固定的，
        /// 长时间看仍是"整齐地错开"。0.12 ≈ 人眼刚好读不出快慢差。</summary>
        public static float ShroudSpeedJitter = 0.12f;

        // ---------------------------------------------- 场域氛围件（ambient_*）
        //
        // 【这是哪一类】不挂任何一张卡、源点钉在**主战场地面中心**、靠世界尺度
        // 铺满视野的整场氛围（雷暴/风沙/极光…）。与罩身（包住一张卡）、
        // 地面件（落在某张卡的定位圆）都不同，故几何参数单独一组放这里。
        // 落盘用途＝`VfxUsage.AmbientField`；挂载走 `UnitAuraService`（按 key 全场
        // 去重＋持有者引用计数：三个人身上都有【雷霆】也只有一份雷暴）。

        /// <summary>整件放大倍数。厂包件按单人身位做（数米级），要铺满战场必须放大；
        /// 过大则粒子稀疏、近处穿帮。3.5 是照 Effect19 电弧在 6 等分战场上的覆盖定的，
        /// 换原料要回来重标。</summary>
        public static float AmbientFieldScale = 3.5f;

        /// <summary>源点相对地面中心抬高（世界单位）。0＝贴地；抬高一点让游离元素
        /// 从卡牌之间穿过而不是全埋在地里。单个源可用 <see cref="AmbientFieldSource.Lift"/>
        /// 覆盖（`float.NaN`＝沿用本值）。</summary>
        public static float AmbientFieldLift = 0.4f;

        /// <summary>全局密度倍数：作用在**所有**场域源的粒子发射量上（rate 与 burst）。
        /// 想整体"雷更密/更稀"只调这一个数；单源的疏密走 <see cref="AmbientFieldSource.Density"/>。
        ///
        /// 与画质档是两回事：档位是**设备**维度（同一观感在弱机上变稀），
        /// 密度是**演出**维度（这场雷暴该有多凶）。两者相乘，互不干扰。</summary>
        public static float AmbientFieldDensity = 1.8f;

        /// <summary>一个场域源＝这件氛围的一处发生地。
        ///
        /// 【为什么要多源】单源钉死在地面中心的雷暴，看久了是"中心一团一直在闪"——
        /// 观众读到的是一个循环动画，不是一场雷暴。雷暴的观感来自**发生地不断变**
        /// 且**远近有层次**：近处往战场里劈、远处天边闷闪。所以拆成一组源，
        /// 每个源自己的位置/尺度/疏密/游走都能单独配。</summary>
        public struct AmbientFieldSource
        {
            /// <summary>只用于层级里的节点名，方便在 Hierarchy 里认出是哪一处。</summary>
            public string Name;
            /// <summary>横向偏移，× <c>BattlefieldLayout.MainHalfWidth</c>（0＝正中）。</summary>
            public float X;
            /// <summary>纵深偏移，× 主战场半纵深（正＝往后院/远景推，>1 即出主战场）。</summary>
            public float Z;
            /// <summary>抬高（世界单位）。`NaN`＝用 <see cref="AmbientFieldLift"/>。</summary>
            public float Lift;
            /// <summary>尺度，× <see cref="AmbientFieldScale"/>。远景源要大一点才有天幕感。</summary>
            public float Scale;
            /// <summary>疏密，× <see cref="AmbientFieldDensity"/>。</summary>
            public float Density;
            /// <summary>随机换点半径，× <c>MainHalfWidth</c>；0＝钉死不动。
            /// 这是"多发生在这一带"的实现：源在自己的圈里跳，而不是每次都在同一点。</summary>
            public float WanderRadius;
            /// <summary>换点间隔（秒）。太短会读成抖动，太长又回到"钉死"。</summary>
            public float WanderInterval;
            /// <summary>绕 Y 轴自转（度）。多个源用同一件时，转一下角度就不会看出
            /// 是同一个动画在三个地方复读。</summary>
            public float Yaw;
            /// <summary>本源要关掉的层（按节点名前缀匹配，大小写不敏感）。
            /// 用途是**语义不成立的层**，不是省性能：悬在天上的源不该有地面接触痕
            /// （`ImpactDecal` 会变成半空中的一块光斑）。省性能走档位与密度。</summary>
            public string[] HideLayers;
        }

        /// <summary>场域源清单（默认两处：战场一处、背景一处，**都是自上而下劈**）。
        /// 加/删一处就是往这个数组里加/删一行，挂载与释放自动跟着走。
        ///
        /// 默认值的取法：两处的游走半径都给到 ≥1 倍主战场半宽，即**落点铺满各自那一带**
        /// 而不是围着一个点小幅晃——雷暴的观感来自"到处都在劈"，钉在一小圈里
        /// 反而会被读成一台在原地循环的机器。背景那处推到主战场之外并抬高，
        /// 尺度更大、疏密略低、换点更勤，读作更远更零星；层序仍是负值不盖卡面。</summary>
        public static AmbientFieldSource[] AmbientFieldSources =
        {
            new() { Name = "战场", X = 0f, Z = 0f, Lift = float.NaN, Scale = 1f,
                    Density = 1f, WanderRadius = 1.3f, WanderInterval = 0.5f },
            new() { Name = "背景", X = 0f, Z = 1.7f, Lift = 7f, Scale = 1.35f,
                    Density = 0.7f, WanderRadius = 2.0f, WanderInterval = 0.45f, Yaw = 180f,
                    HideLayers = new[] { "ImpactDecal" } },
        };

        /// <summary>渲染层序。氛围是**背景**：压到卡牌之下（负值），否则满屏元素
        /// 盖在卡面前会把主体（立绘/兵力）糊掉，读作"看不清在打什么"。</summary>
        public static int AmbientFieldSortingOrder = -5;

        // ---------------------------------------------- 单挑三处厂包特效
        //
        // 【为什么 key 放这里而不是 PerformanceProfile】Profile 是**按武将/战法**
        // 查 key 的表，而这三件是单挑这一段演出的固定构件，与谁参战无关，
        // 没有可查的行。放这里同样满足"演出代码不硬编码 key"（DuelStage 只读本类）。
        // 三件都必须先按 docs/client/vfx_standardization.md 落成 Resources 标准件，
        // 落盘脚本：`GreekMyth/特效/接线 单挑三件`（WireDuelStageVfx.cs）。

        /// <summary>出阵地面：**出框之前**在两名武将各自的定位圆上炸开（Effect28）。
        /// 来源 RFX4 `Effect28`（画廊 3/8 包 19/54 件）。</summary>
        public static string DuelLaunchVfxKey = "cast_duel_launch";

        /// <summary>出阵卡面追加：与地面 Effect28 **同时**挂在双方卡面上。
        /// 画廊 1/8（我方标准件）件 8/60＝`aura_duel_victory`（同件兼作胜者加冕）。</summary>
        public static string DuelLaunchCardVfxKey = "aura_duel_victory";

        /// <summary>加冕：撤镜还位后在胜者**卡面**上播。
        /// 原料 RFX4 Effect23 碰撞子件。</summary>
        public static string DuelWinnerVfxKey = "aura_duel_victory";

        /// <summary>溃败地面：撤镜还位后在败者**定位圆**留痕。
        /// 原料 Magic Pack v1 Effect8 碰撞子件；贴花由自研裂地补。</summary>
        public static string DuelLoserVfxKey = "ground_duel_defeat";

        /// <summary>溃败卡面追加：与地面溃败**同时**挂在败者卡面上。
        /// 画廊 1/8 件 32/60 观感；同原料 Effect8 的 Anchor 标准件（无 VfxGroundLayer）。</summary>
        public static string DuelLoserCardVfxKey = "aura_duel_defeat";

        // ---------------------------------------------- 三件的「等它播完」规则
        //
        // 单挑的三件是**顺序播**的：出阵放完才飞出去，落回卡框才放胜负两件、
        // 放完单挑才算结束。等待时长不写死，而是运行期从 prefab 探**发射窗口**
        // （`VFXManager.EmitWindow`：各粒子系统 duration 的最大值，**不含烟尾**）。
        //
        // 【为什么等的是真实秒、不过 ctx.Scaled】粒子按真实时间播。把这段乘倍速
        // 等于把特效拦腰截断——那就不叫"播完再走"了。代价是 4× 快进时这三拍
        // 不会跟着变快，所以上限必须卡住。

        /// <summary>单件等待上限（秒）。厂包件时长不可控，演出不能被一个 6 秒的件卡住。
        ///
        /// 1.7 s 是照着实际素材定的，不是拍脑袋：`cast_duel_launch` 掐掉 1.0 s 空转
        /// 前摇后，一次性爆发层的发射窗口是 1.5 s，上限必须**高于**它，否则等待被
        /// 上限截断、人在爆发正盛时飞出去（2026-07-27 症状：「特效没跑完就飞了」）。
        /// 改素材后要回来核对：`EmitWindow` 实测值 ≤ 本值，否则又会截断。</summary>
        public static float DuelVfxWaitCap = 1.7f;

        /// <summary>探不到时长时（key 未落盘 / 件里没粒子）的保底节拍（秒）。
        /// **不能因为素材缺失就把节奏也丢了**——没有它，缺件时这一拍会整个消失，
        /// 前面的蓄力就白蓄了（占位三级回退的时序版）。</summary>
        public static float DuelVfxFallbackSeconds = 0.45f;

        /// <summary>回收时长在等待时长之外多给的余量（秒）＝留给烟尾飘完。
        /// 等的是发射窗口，回收却不能卡在窗口末尾，否则余烬会被硬切。</summary>
        public static float DuelVfxTailSeconds = 1.6f;

        /// <summary>暗幕不透明度。单挑是全场唯一"停下来看"的时刻，压得比
        /// 单人 cut-in（0.55）更黑，观众视线才会锁在展示屏上。</summary>
        public static float DuelVeilAlpha = 0.78f;

        /// <summary>中央单挑图标浮现时长（秒）。</summary>
        public static float DuelIconSeconds = 0.20f;

        /// <summary>一次交错「对穿出去」的时长（秒）。线性推进＝高速掠过感。</summary>
        public static float DuelCrossSeconds = 0.30f;

        /// <summary>交错后弹回本位的时长 = <see cref="DuelCrossSeconds"/> × 此值。
        /// 比出去略短，读作"弹回来站定"而不是"慢慢挪回来"。</summary>
        public static float DuelCrossReturnRatio = 0.72f;

        /// <summary>交错时两人各自的弧高 = 槽位到中心距离 × 此值（一上一下）。
        /// **不要设 0**：正对穿会让两张立绘在中点完全重叠，读不出错身。</summary>
        public static float DuelCrossArc = 0.22f;

        /// <summary>交错白闪的峰值不透明度。</summary>
        public static float DuelClashFlashAlpha = 0.55f;

        /// <summary>单轮战斗动作的播放时长（秒）＝ flipbook 一遍走完的时间。
        /// 无 flipbook 资源时就是静态立绘占满这段时间。</summary>
        public static float DuelActionSeconds = 1.0f;

        /// <summary>末轮（双方同时 strike 的高潮）动作时长倍率。</summary>
        public static float DuelFinalRoundScale = 1.35f;

        /// <summary>定胜负演出时长与其后的停留时长（秒）。</summary>
        public static float DuelResultSeconds = 0.45f;
        public static float DuelResultHoldSeconds = 0.55f;

        /// <summary>胜者上抬距离 = 其立绘缩放 × 此值；败者下沉 0.6 倍。</summary>
        public static float DuelWinnerLift = 0.35f;

        /// <summary>胜 / 败者定格缩放倍率。</summary>
        public static float DuelWinnerScale = 1.10f;
        public static float DuelLoserScale = 0.93f;

        /// <summary>立绘槽位中心：横向 ×半宽、纵向 ×半高。</summary>
        public static float DuelSlotX = 0.42f;
        public static float DuelSlotY = 0.04f;

        /// <summary>立绘槽位尺寸：×半宽 / ×半高（contain 等比放入）。</summary>
        public static float DuelSlotWidth = 0.70f;
        public static float DuelSlotHeight = 1.10f;

        /// <summary>虚空展示屏尺寸：×半宽 / ×半高。宽度可超过 2（出血到屏外），
        /// 高度留白才看得出这是一块"屏"而不是又一层暗幕。</summary>
        public static float DuelScreenWidth = 1.90f;
        public static float DuelScreenHeight = 1.32f;

        /// <summary>中央单挑图标边长 ×半高。</summary>
        public static float DuelIconSize = 0.30f;

        // ------------------------------------------------------------ 单挑展示屏华饰
        //
        // 【为什么这些参数值得存在】静止的纯色底 + 静止的立绘 = 一张贴纸。
        // 人眼判定"活"靠的是**多个速率不同的运动叠在一起**：下面刻意配了四种
        // 周期——放射慢转 / 浮尘上升 / 边框呼吸 / 整屏推进，周期互质，
        // 任意两帧都不重样。**调参时不要把它们调成同一个节奏**，会立刻塌回呆板。
        // 实现 VFX/DuelStageChrome.cs。

        /// <summary>影院黑边高度 ×半高（上下各一条）。进场压下、退场收起——
        /// 最省事也最有效的"过场"信号：画幅变了，观众自己就知道是重头戏。</summary>
        public static float DuelLetterboxHeight = 0.13f;

        /// <summary>左右阵营辉光的不透明度（横向渐变，向屏心衰减）。
        /// 兼作阵营识别：一眼读出这半边是谁。</summary>
        public static float DuelFactionGlowAlpha = 0.30f;

        /// <summary>放射光芒：条数（实际光道数 = ×2）、光盘半径 ×半高、
        /// 不透明度、自转速度（度/秒，左右反向——同向会读成整体在旋转）。
        /// **半径不要超过 `DuelScreenHeight/2`**，无遮罩，出屏会糊到暗幕上。</summary>
        public static int DuelRayCount = 9;
        public static float DuelRayRadius = 0.55f;
        public static float DuelRayAlpha = 0.20f;
        public static float DuelRaySpinDegPerSec = 6f;

        /// <summary>四角纹饰边长 ×半高。</summary>
        public static float DuelCornerSize = 0.16f;

        /// <summary>浮尘余烬：数量、上升速度 ×半高/秒、不透明度基准。
        /// 一半排在立绘之前、一半之后，才有纵深而不是一层贴纸。</summary>
        public static int DuelEmberCount = 22;
        public static float DuelEmberRiseSpeed = 0.14f;
        public static float DuelEmberAlpha = 0.55f;

        /// <summary>屏边框呼吸频率（Hz）。慢，且**不要**与浮尘/推进同步。</summary>
        public static float DuelRimBreathHz = 0.42f;

        /// <summary>整屏极缓推进：终点缩放与到达时长（秒）。单帧察觉不到，
        /// 连起来就是"镜头在压过来"。调大到 ~1.1 以上会看出屏在长大。</summary>
        public static float DuelPushInScale = 1.045f;
        public static float DuelPushInSeconds = 7f;

        /// <summary>交错冲击环：扩张时长（秒）与终点倍率（×图标边长）。</summary>
        public static float DuelImpactRingSeconds = 0.45f;
        public static float DuelImpactRingScale = 2.6f;

        /// <summary>立绘背光：同一张图放大描边、按阵营色垫在立绘之后。
        /// 无 shader 的"边缘发光"，把立绘从背景里拔出来。</summary>
        public static float DuelPortraitGlowAlpha = 0.5f;
        public static float DuelPortraitGlowScale = 1.06f;

        /// <summary>立绘在展示屏上的待机呼吸：频率（Hz）、纵向幅度（×自身缩放）。
        /// 交错与动作之间若完全静止就是死帧（R-4.1），这一条兜住。</summary>
        public static float DuelPortraitBreathHz = 0.5f;
        public static float DuelPortraitBreathAmp = 0.02f;
    }
}
