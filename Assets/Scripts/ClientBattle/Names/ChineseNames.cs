using System.Collections.Generic;
using System.Text;

namespace ClientBattle.Names
{
    // =========================================================================
    // 中文显示名注册表：战法/状态 id → 中文名（飘字/图标/日志用）。
    // 与后端 battle/names.py 保持同步——后端新增战法/状态时此处同步登记；
    // 未登记的 id 原样显示（不报错），并由 VFXResolver LogWarning 提示。
    // =========================================================================

    public static class ChineseNames
    {
        static readonly Dictionary<string, string> Skills = new()
        {
            ["basic_attack"] = "普攻",
            // ---- 神阵营 ----
            ["thunder_oracle"] = "雷霆神谕", ["zeus_bolt"] = "天雷击",
            ["athena_aegis"] = "埃癸斯圣盾", ["athena_guard"] = "神盾格挡",
            ["ares_warfury"] = "战神怒火", ["ares_roar"] = "血性咆哮",
            ["hermes_oracle"] = "赫尔墨斯神谕", ["hermes_jest"] = "神使戏言",
            ["delphi_revelation"] = "德尔斐启示", ["apollo_blessing"] = "日光祝祷",
            ["asclepius_oracle"] = "蛇杖庇护圣谕", ["asclepius_kiss"] = "灵蛇之吻",
            ["artemis_hunt"] = "月影狩猎", ["artemis_arrow"] = "猎月之矢",
            ["nike_wings"] = "胜利羽翼", ["nike_paean"] = "凯歌",
            // ---- 人阵营 ----
            ["achilles_wrath"] = "阿喀琉斯之怒", ["achilles_thrust"] = "怒火突刺",
            ["heracles_trials"] = "十二试炼", ["heracles_counter"] = "狮皮反击",
            ["odysseus_trojan"] = "木马奇谋", ["odysseus_feint"] = "声东击西",
            ["perseus_relics"] = "神器三借", ["perseus_mirror"] = "镜盾辉映",
            ["perseus_flash"] = "镜盾闪袭",
            ["atalanta_swift"] = "疾风女猎", ["atalanta_dash"] = "疾走",
            ["paris_fatal_arrow"] = "致命一矢", ["paris_heelseek"] = "觅踵",
            ["ajax_shield"] = "七重牛皮盾", ["ajax_bulwark"] = "坚壁",
            ["chiron_medicine"] = "贤者医术", ["chiron_maxim"] = "导师箴言",
            // ---- 海阵营 ----
            ["poseidon_oracle"] = "海神三叉戟", ["poseidon_torrent"] = "怒涛",
            ["amphitrite_tide"] = "潮汐抚愈", ["amphitrite_grace"] = "海后之泽",
            ["triton_horn"] = "海嗣号角", ["triton_surge"] = "浪涌",
            ["siren_song"] = "魅音", ["siren_charm"] = "魅惑术",
            ["scylla_maw"] = "六首撕咬", ["scylla_bite"] = "撕咬",
            ["charybdis_maw"] = "漩涡巨口", ["charybdis_swallow"] = "吞流",
            // ---- 冥阵营 ----
            ["hades_underworld_dominion"] = "冥域君临", ["hades_soul_drain"] = "冥河汲魂",
            ["medusa_gaze"] = "石化凝视", ["medusa_glance"] = "蛇瞳一瞥",
            ["persephone_seasons"] = "冬春轮转", ["persephone_sprout"] = "春芽",
            ["charon_ferry"] = "渡魂船费", ["charon_ferryman"] = "摆渡",
            ["thanatos_scythe"] = "死神镰痕", ["thanatos_gaze"] = "死亡凝望",
            ["cerberus_bite"] = "三首噬咬", ["cerberus_guard"] = "守门恶犬",
        };

        static readonly Dictionary<string, string> Statuses = new()
        {
            ["silence"] = "缄默", ["disarm"] = "缴械", ["ming_lock"] = "冥锁",
            ["petrify"] = "石化", ["hesitation"] = "犹豫", ["block"] = "格挡",
            ["charm"] = "魅惑", ["first_strike"] = "先攻",
            // ---- 神 ----
            ["thunder"] = "雷霆", ["divine_revelation"] = "神示",
            ["aegis_shield"] = "埃癸斯圣盾", ["athena_guard"] = "神盾格挡",
            ["blood_battle"] = "血战", ["ares_might"] = "战神之勇",
            ["ares_roar_vulnerable"] = "咆哮·破胆",
            ["hermes_confusion_mark"] = "扰心印记", ["hermes_herald_mark"] = "神使印记",
            ["sun_blessing"] = "日光祝祷",
            ["snake_staff_protection"] = "蛇杖庇护", ["snake_staff_tender"] = "灵蛇看护",
            ["moon_hunt"] = "月影狩猎",
            ["nike_wings"] = "胜利羽翼", ["nike_paean_speed"] = "凯歌·迅捷",
            // ---- 人 ----
            ["achilles_wrath"] = "阿喀琉斯之怒", ["achilles_thrust_crit"] = "突刺·锐锋",
            ["heracles_trials"] = "十二试炼",
            ["lion_counter"] = "狮皮反击", ["lion_weaken"] = "狮皮·削力",
            ["trojan_scheme"] = "木马奇谋", ["trojan_bomb"] = "木马·伏兵",
            ["perseus_mirror"] = "镜盾辉映", ["perseus_evade"] = "神器·闪避",
            ["perseus_flash_evade"] = "镜盾·闪袭",
            ["atalanta_swift"] = "疾风女猎",
            ["atalanta_dash_speed"] = "疾走·增速", ["atalanta_dash_damage"] = "疾走·蓄势",
            ["paris_fatal_arrow"] = "致命一矢", ["paris_heelseek"] = "觅踵",
            ["ajax_shield"] = "七重牛皮盾", ["ajax_bulwark_command"] = "坚壁·固守",
            ["chiron_maxim"] = "导师箴言",
            // ---- 海 ----
            ["poseidon_tide"] = "海神", ["flood"] = "怒涛",
            ["amphitrite_grace"] = "海后之泽",
            ["triton_horn_command"] = "号角·固甲", ["triton_surge"] = "浪涌",
            ["charybdis_maw"] = "漩涡巨口", ["charybdis_swallow"] = "吞流",
            // ---- 冥 ----
            ["styx_blood_oath"] = "冥河血誓", ["shadow_veil"] = "幽影蔽体",
            ["hades_command_drain"] = "冥祭献统", ["hades_command_loss"] = "冥祭献统·被汲",
            ["hades_int_gain"] = "冥祭献统·聚智",
            ["soul_drain_gain"] = "汲魂·得", ["soul_drain_loss"] = "汲魂·失",
            ["medusa_gaze"] = "石化凝视",
            ["medusa_int_loss"] = "蛇瞳夺智·被夺", ["medusa_int_gain"] = "蛇瞳夺智",
            ["persephone_seasons"] = "冬春轮转",
            ["charon_ferry"] = "渡魂船费", ["charon_int_gain"] = "船资·聚智",
            ["thanatos_death_gaze"] = "死亡凝望", ["cerberus_guard"] = "守门恶犬",
        };

        static readonly Dictionary<string, string> Attrs = new()
        {
            ["force"] = "武力", ["intelligence"] = "智力",
            ["command"] = "统率", ["speed"] = "敏捷",
        };

        public static string Skill(string id) => Skills.TryGetValue(id, out var n) ? n : id;
        public static string Status(string id) => Statuses.TryGetValue(id, out var n) ? n : id;
        public static string Attr(string id) => Attrs.TryGetValue(id, out var n) ? n : id;

        /// <summary>飘字动态字体预热字符集：所有已登记中文名 + 数字/结算标记。
        /// 开战前一次请求字形，避免首次出现某战法名时主线程同步扩充字体纹理。</summary>
        public static string FloatingTextCharacters()
        {
            var unique = new HashSet<char>();
            var result = new StringBuilder();
            void Add(string value)
            {
                foreach (char c in value)
                    if (unique.Add(c)) result.Append(c);
            }
            foreach (var value in Skills.Values) Add(value);
            foreach (var value in Statuses.Values) Add(value);
            foreach (var value in Attrs.Values) Add(value);
            Add("0123456789+-! 暴击格挡闪避反弹无法行动主将阵亡");
            return result.ToString();
        }
    }
}
