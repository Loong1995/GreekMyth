using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>画质档。按**设备能力**分，不按平台分（同是安卓，旗舰与千元机差一个数量级）。</summary>
    public enum VfxTier
    {
        /// <summary>低端安卓（≈4GB 内存以下 / 老 Adreno 6xx 以下）。</summary>
        Low,
        /// <summary>中端（主力机型），编辑器默认按它模拟。</summary>
        Mid,
        /// <summary>高端手机 / PC。厂包原始强度。</summary>
        High,
    }

    // =========================================================================
    // 特效画质分档（唯一配置点）。
    //
    // 【原则：只降强度，不删效果】接件时**不允许**把某一层从成品里摘掉来省性能
    // （摘了就再也回不来，中高端机也白白损失）。落盘一律保留原始强度，
    // 运行期由 `VfxTierScale` 按本表的系数缩放。想调平衡点＝改本文件的三张系数，
    // 全项目所有件同时生效，不用重接任何一件。
    //
    // 唯一例外是**多余实时灯**：灯的开销来自"多一盏就多一遍光照循环"，
    // 调 intensity 一分钱都省不下来，只能开/关。故第 2 盏起挂 MinTier=High。
    //
    // 【编辑器与真机一致】编辑器里 Play 走的是 PC RP 资产（Mobile 档对 Standalone
    // 平台屏蔽），所以**分辨率/后处理层面**编辑器天然比真机好；但特效这一层
    // 我们让它一致：编辑器默认按 `EditorTier`（中端）跑同一套系数。
    // 想预览低端观感就把 EditorTier 改成 Low，不需要真机。
    //
    // 【档位是给玩家的】本档最终要挂到游戏内设置面板（自动/低/中/高），
    // 玩家选择存 PlayerPrefs、下次启动沿用（`LoadUserPreference`）。
    // 允许玩家调的只有**强度档**；**不提供"关闭某类特效"的开关**——
    // 那类开关一旦存在，策划/演出就会依赖"反正玩家能关"，而战报演出的每一件
    // 都承载信息（命中/罩身/裂地都是读战况的线索），关掉＝看不懂在打什么。
    // 唯一可以彻底不出现的，是**本来就画不出来或普遍不成立的机制**
    // （Legacy Projector 贴花在 URP 下根本不渲染），那不是"关效果"，是清死层。
    // =========================================================================

    public static class VfxQuality
    {
        /// <summary>编辑器 Play 时模拟的真机档（默认中端＝主力机型）。
        ///
        /// 不要改这里的默认值来切档——菜单 `GreekMyth/特效/画质档` 存 EditorPrefs
        /// 并写回本字段，改代码会和菜单打架（下次域重载又被菜单值覆盖）。</summary>
        public static VfxTier EditorTier = VfxTier.Mid;

        /// <summary>粒子发射系数：作用于所有粒子层的 rateOverTime 与 burst 数量。
        /// 半透明加色粒子按屏幕覆盖面积计价，是移动端掉帧第一死因，所以这是
        /// 最主要的一把闸。等比缩放保形状保节奏，只变稀。</summary>
        public static readonly float[] ParticleFactor = { 0.40f, 0.70f, 1.00f };

        /// <summary>屏幕折射层系数（同样作用于发射量）。折射本身在我们的低频
        /// 大理石舞台上收益有限（P-74），但盾/火的热浪确实靠它，故低端只压不删。</summary>
        public static readonly float[] RefractionFactor = { 0.30f, 0.70f, 1.00f };

        /// <summary>实时灯亮度系数（第 1 盏；第 2 盏起走 MinTier 开关）。</summary>
        public static readonly float[] LightFactor = { 0.60f, 0.85f, 1.00f };

        // ------------------------------------------------ 镜头层（全屏 pass）
        //
        // Bloom 的成本与粒子数无关（它是几遍全屏降采样滤波），所以**不受
        // VfxTierScale 管辖** —— 只能由 BattlePostFx 按当前档直接写 Volume。
        // 这是低端机上最贵的几项之一，此前与档位完全脱钩。
        //
        // 红线：低端只降强度、关高质量滤波，**不关 Bloom**。厂包峰值件按
        // HDR+Bloom 设计，关掉会塌成廉价喷洒，自研裂地的熔岩锋面（HDR 分量 >1）
        // 也会直接变成一条橙线 —— 那是"删效果"，违反本文件开头的原则。

        /// <summary>Bloom 阈值：越高越少东西溢光。低端只让真正的峰值（熔岩/巨伤）溢出，
        /// 卡面日常亮部不参与，顺带把画面对比度还回来一点。</summary>
        public static readonly float[] BloomThreshold = { 1.05f, 0.95f, 0.85f };

        /// <summary>Bloom 强度。</summary>
        public static readonly float[] BloomIntensity = { 0.75f, 0.95f, 1.15f };

        /// <summary>高质量滤波＝Bloom 真正花钱的地方（多几遍降采样滤波）。
        /// 关掉**不改变"有没有溢光"**，只是光晕边缘略糙，故低/中端一律关。
        /// 这是"只降强度不删效果"在镜头层的正确落法。</summary>
        public static readonly bool[] BloomHighQuality = { false, false, true };

        static VfxTier? _current;

        /// <summary>当前档。编辑器取 <see cref="EditorTier"/>，真机按硬件探。</summary>
        public static VfxTier Current
        {
            get
            {
                _current ??= Application.isEditor ? EditorTier : Detect();
                return _current.Value;
            }
        }

        /// <summary>手动指定档（真机调试 / 设置面板）。改档后已在场的件下次启用时生效。</summary>
        public static void Override(VfxTier tier) => _current = tier;

        const string PrefKey = "vfx_tier"; // 空/缺省＝自动探测

        /// <summary>玩家在设置面板里选档（`null`＝自动，按硬件探）。立即生效并持久化。</summary>
        public static void SetUserPreference(VfxTier? tier)
        {
            if (tier == null)
            {
                PlayerPrefs.DeleteKey(PrefKey);
                _current = Application.isEditor ? EditorTier : Detect();
            }
            else
            {
                PlayerPrefs.SetInt(PrefKey, (int)tier.Value);
                _current = tier.Value;
            }
            PlayerPrefs.Save();
        }

        /// <summary>玩家当前选择（`null`＝自动）。设置面板回显用。</summary>
        public static VfxTier? UserPreference =>
            PlayerPrefs.HasKey(PrefKey) ? (VfxTier)PlayerPrefs.GetInt(PrefKey) : null;

        /// <summary>启动时调用：有玩家选择就用玩家的，否则自动探。
        /// 顺带把判据打进日志——真机上"为什么是这个档"只能靠这一行回答。</summary>
        public static void LoadUserPreference()
        {
            var pref = UserPreference;
            _current = pref ?? (Application.isEditor ? EditorTier : Detect());
            Debug.Log("[VfxQuality] " + Describe());
        }

        public static float Factor(float[] table) => table[(int)Current];

        /// <summary>档位索引（镜头层等需要直接取表的地方用）。</summary>
        public static int Index => (int)Current;

        /// <summary>探档依据的原始数据（真机排查用，`Describe()` 会打出来）。</summary>
        public static string Describe() =>
            $"tier={Current}（{(UserPreference != null ? "玩家选择" : Application.isEditor ? "编辑器模拟" : "自动探")}）"
            + $" 粒子×{Factor(ParticleFactor):0.00} 折射×{Factor(RefractionFactor):0.00}"
            + $" bloom={BloomIntensity[Index]:0.00}/阈{BloomThreshold[Index]:0.00}"
            + $"{(BloomHighQuality[Index] ? "/高质滤波" : "")}"
            + $" mem={SystemInfo.systemMemorySize}MB vram={SystemInfo.graphicsMemorySize}MB"
            + $" gfx={SystemInfo.graphicsDeviceType} device={SystemInfo.deviceModel}";

        /// <summary>硬件探档。判据取**内存**为主、显存只作降级信号：型号白名单永远过期，
        /// 内存与"能不能扛住满屏半透明粒子"的相关性最直接。
        ///
        /// 【为什么显存不能当硬判据】`graphicsMemorySize` 在移动端是**估算值**，
        /// 各家 UMA 设备报得五花八门（iOS 常报成系统内存的一个分数）。原先写的
        /// `mem<=4096 || vram<=1024 → Low` 会把 6GB 的 iPhone 一并打成低端——
        /// 用一个不可靠的量做"或"降级，等于让最不准的那个数说了算。
        /// 现在只在**安卓**上、且报了个可信小值时降一档。</summary>
        static VfxTier Detect()
        {
            int mem = SystemInfo.systemMemorySize;      // MB
            int vram = SystemInfo.graphicsMemorySize;   // MB

            var tier = mem <= 4096 ? VfxTier.Low
                     : mem <= 7168 ? VfxTier.Mid
                     : VfxTier.High;

            if (Application.platform == RuntimePlatform.Android && vram > 0 && vram <= 1024 && tier > VfxTier.Low)
                tier = (VfxTier)((int)tier - 1);

            return tier;
        }
    }
}
