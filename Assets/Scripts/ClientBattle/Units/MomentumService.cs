using System.Collections.Generic;
using ClientBattle.Events;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第4层 演出执行·B1/B2】MomentumService（静态镜像账本）：
    //
    // - momentum_change 事件 → 四轨分值镜像（零结算：value 直接取事件权威值）。
    // - 计分按轨类型跨技能累计（与服务端一致：active 轨叠所有主动释放，
    //   不是每个技能一条独立条）。
    // - 表现分级注册表（TrackTable）：轨 → tint / 显示序，加轨=加条目，
    //   禁止在播放流程写死轨名（phase4_plan 注册表驱动通则）。
    // - 分档：0~3 半亮；≥Flash(4) 首次白闪爆发；≥Full(5) 常驻 rim +
    //   事件 cut_in 驱动切入（服务端 value≥5 带 cut_in）。
    // - 服务器在武将自身行动窗开始时四轨静默清零（不发事件），客户端在
    //   action_start 落账处调 OnActionStart 同步镜像。
    // =========================================================================

    public static class MomentumService
    {
        public const int Full = 5;   // 满档：cut_in + 常驻流光
        public const int Flash = 4;  // 闪光档：首次白闪（客户端表现，无事件字段）

        public class TrackStyle
        {
            public string Track;
            public string Label;   // 单字标签（势能条 tooltip/cut-in 文案用）
            public Color Tint;
            public int Order;      // 势能条显示序
        }

        /// <summary>表现分级注册表：新增轨只需加条目（服务端注册表的镜像）。</summary>
        public static readonly Dictionary<string, TrackStyle> TrackTable = new()
        {
            ["active"] = new TrackStyle { Track = "active", Label = "主", Order = 0,
                Tint = new Color(0.95f, 0.78f, 0.25f) },   // 暖金
            ["passive"] = new TrackStyle { Track = "passive", Label = "被", Order = 1,
                Tint = new Color(0.45f, 0.75f, 0.55f) },   // 铜绿
            ["oracle"] = new TrackStyle { Track = "oracle", Label = "谕", Order = 2,
                Tint = new Color(0.62f, 0.42f, 0.95f) },   // 雷紫
            ["basic_pursuit"] = new TrackStyle { Track = "basic_pursuit", Label = "击", Order = 3,
                Tint = new Color(0.9f, 0.32f, 0.25f) },    // 赤红
        };

        // hero_id → track → value（镜像账本；value 以事件为准）
        static readonly Dictionary<string, Dictionary<string, int>> Values = new();
        // hero_id → 已播过首次闪光爆发的轨（闪光档期间不重复爆发）
        static readonly Dictionary<string, HashSet<string>> OverflowShown = new();

        /// <summary>全局势能 = 双方全部武将四轨之和（BGM 分层档位输入，C8）。</summary>
        public static int GlobalTotal { get; private set; }

        public static int ValueOf(string heroId, string track) =>
            Values.TryGetValue(heroId, out var t) && t.TryGetValue(track, out var v) ? v : 0;

        /// <summary>落账一条 momentum_change：更新镜像 + 驱动单位势能条/溢出表现。
        /// silent=true（跳到结尾/静默落账）时只记账不演出。</summary>
        public static void Apply(MomentumChangeEvent ev, UnitView unit, bool silent = false)
        {
            if (ev.HeroId == null || !TrackTable.ContainsKey(ev.Track)) return;
            if (!Values.TryGetValue(ev.HeroId, out var tracks))
                Values[ev.HeroId] = tracks = new Dictionary<string, int>();
            tracks.TryGetValue(ev.Track, out int before);
            tracks[ev.Track] = ev.Value; // 事件权威值，不做客户端加法
            GlobalTotal += ev.Value - before;
            Audio.BgmLayerService.Instance?.SetGlobalMomentum(GlobalTotal);

            if (unit == null) return;
            unit.SetMomentum(ev.Track, ev.Value);
            if (silent) return;

            // 4 分闪光：该轨首次跨过 Flash 档播白闪（与 cut_in 门槛 Full 分离）
            if (ev.Value >= Flash)
            {
                if (!OverflowShown.TryGetValue(ev.HeroId, out var shown))
                    OverflowShown[ev.HeroId] = shown = new HashSet<string>();
                if (shown.Add(ev.Track))
                    unit.PlayMomentumOverflow(TrackTable[ev.Track].Tint);
            }
        }

        /// <summary>武将自身行动窗开始：四轨镜像清零（与服务器静默清零同步），
        /// 常驻流光与闪光记录一并撤除。</summary>
        public static void OnActionStart(string heroId, UnitView unit)
        {
            if (heroId == null) return;
            if (Values.TryGetValue(heroId, out var tracks))
                foreach (var v in tracks.Values) GlobalTotal -= v;
            Values.Remove(heroId);
            OverflowShown.Remove(heroId);
            unit?.ClearMomentum();
            Audio.BgmLayerService.Instance?.SetGlobalMomentum(GlobalTotal);
        }

        /// <summary>整局/整场重置。</summary>
        public static void ClearAll()
        {
            Values.Clear();
            OverflowShown.Clear();
            GlobalTotal = 0;
            Audio.BgmLayerService.Instance?.SetGlobalMomentum(0);
        }
    }
}
