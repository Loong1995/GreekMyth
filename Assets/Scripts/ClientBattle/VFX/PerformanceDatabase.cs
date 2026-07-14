using System.Collections.Generic;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 演出配置库（ScriptableObject）：三级策略的数据源。
    //   特殊配置（每战法/状态一条 PerformanceProfile，最高优先级）
    //   → 组默认（主动/普攻/追击/状态触发/神谕 各一条）
    //   → 全默认（兜底，任何情况必能播出东西）
    //
    // 资产路径：Resources/ClientBattle/PerformanceDatabase.asset（Inspector 可改）；
    // 资产缺失时用 BuildRuntimeDefault() 代码默认值（当前全部武将战法的特殊
    // 配置就写在这里，等你后续在 Inspector 里覆盖/上传真实资源）。
    // =========================================================================

    [CreateAssetMenu(menuName = "GreekMyth/Performance Database", fileName = "PerformanceDatabase")]
    public class PerformanceDatabase : ScriptableObject
    {
        [Header("特殊配置（skillId/statusId 精确匹配，最高优先级）")]
        public List<PerformanceProfile> SpecialProfiles = new();

        [Header("组默认")]
        public PerformanceProfile ActiveDefault;        // 主动
        public PerformanceProfile MeleeDefault;         // 普攻
        public PerformanceProfile PursuitDefault;       // 追击
        public PerformanceProfile StatusTriggerDefault; // 特殊状态触发
        public PerformanceProfile OracleDefault;        // 神谕/被动宣告

        [Header("全默认（兜底）")]
        public PerformanceProfile GlobalDefault;

        Dictionary<string, PerformanceProfile> _index;

        public PerformanceProfile FindSpecial(string id)
        {
            if (_index == null)
            {
                _index = new Dictionary<string, PerformanceProfile>();
                foreach (var profile in SpecialProfiles)
                    if (!string.IsNullOrEmpty(profile.SkillOrStatusId))
                        _index[profile.SkillOrStatusId] = profile;
            }
            return _index.TryGetValue(id, out var hit) ? hit : null;
        }

        // ---------------------------------------------------------- 加载

        public static PerformanceDatabase LoadOrDefault()
        {
            var asset = Resources.Load<PerformanceDatabase>("ClientBattle/PerformanceDatabase");
            if (asset != null) return asset;
            Debug.Log("[ClientBattle] 未找到 PerformanceDatabase.asset，使用代码内置默认配置");
            return BuildRuntimeDefault();
        }

        /// <summary>代码内置配置：组默认 + client_perform §二~五 全部特殊战法。</summary>
        public static PerformanceDatabase BuildRuntimeDefault()
        {
            var db = CreateInstance<PerformanceDatabase>();

            db.GlobalDefault = new PerformanceProfile
            {
                Template = PerformanceTemplate.Auto,
                ProjectileKey = "slash", HitKey = "hit_generic",
                SfxKey = "sfx_active_default", HitSfxKey = "sfx_hit_default",
            };
            db.ActiveDefault = db.GlobalDefault.Clone();
            db.MeleeDefault = new PerformanceProfile
            {
                Template = PerformanceTemplate.Melee,
                HitKey = "hit_generic", SfxKey = "sfx_melee_default",
            };
            db.PursuitDefault = new PerformanceProfile
            {
                Template = PerformanceTemplate.Auto,   // 群攻走主动、单体走普攻逻辑（模板内判断）
                HitKey = "hit_generic", SfxKey = "sfx_pursuit_default",
            };
            db.StatusTriggerDefault = new PerformanceProfile
            {
                Template = PerformanceTemplate.StatusTrigger,
                ProjectileKey = "magic_bolt", HitKey = "hit_generic",
                SfxKey = "sfx_status_trigger_default",
            };
            db.OracleDefault = new PerformanceProfile
            {
                Template = PerformanceTemplate.OracleAura,
                AuraKey = "aura_generic", SfxKey = "sfx_oracle_default",
            };

            // ---------------- 特殊配置（client_perform §二~五）----------------
            db.SpecialProfiles = new List<PerformanceProfile>
            {
                // 神：雷霆神谕——挂身随机闪电环绕；落雷触发用专属闪电光+音效
                new() { SkillOrStatusId = "thunder_oracle", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_thunder", SfxKey = "sfx_oracle_thunder" },
                new() { SkillOrStatusId = "thunder", Template = PerformanceTemplate.StatusTrigger,
                        ProjectileKey = "lightning_strike", HitKey = "hit_lightning",
                        SfxKey = "sfx_thunder_strike" },
                // 神：埃癸斯圣盾——挂身圣盾特效；反制走普攻逻辑+专属音效
                new() { SkillOrStatusId = "athena_aegis", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_aegis", SfxKey = "sfx_oracle_aegis" },
                new() { SkillOrStatusId = "aegis_shield", Template = PerformanceTemplate.Melee,
                        HitKey = "hit_shield_counter", SfxKey = "sfx_aegis_counter" },
                // 神：战神怒火——全场血红呼吸滤镜（弱），战神之勇获得者强一档（Intensity）
                new() { SkillOrStatusId = "ares_warfury", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_bloodlust_weak", BoardFilterKey = "filter_bloodlust",
                        Intensity = 0.4f, SfxKey = "sfx_oracle_ares" },
                new() { SkillOrStatusId = "ares_might", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_bloodlust_strong", Intensity = 1.0f },
                // 神：德尔斐启示——呼吸阳光特效（强度可调）
                new() { SkillOrStatusId = "delphi_revelation", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_sunlight", Intensity = 0.7f, SfxKey = "sfx_oracle_apollo" },
                // 神：赫尔墨斯神谕——印记图标展示为主
                new() { SkillOrStatusId = "hermes_oracle", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_hermes_mark", SfxKey = "sfx_oracle_hermes" },
                // 神：蛇杖庇护圣谕——无演出
                new() { SkillOrStatusId = "asclepius_oracle", Template = PerformanceTemplate.None },
                // 神：胜利羽翼——暴击机会者呼吸阳光（复用阿波罗资源 key）
                new() { SkillOrStatusId = "nike_wings", Template = PerformanceTemplate.OracleAura,
                        AuraKey = "aura_sunlight", Intensity = 0.7f, SfxKey = "sfx_oracle_nike" },
                // 人：阿喀琉斯之怒——追伤复用单体追击，敌方身上闪超大长矛裂甲图标+专属音效
                new() { SkillOrStatusId = "achilles_wrath", Template = PerformanceTemplate.StatusTrigger,
                        ExtraIconKey = "icon_spear_crack", ExtraIconScale = 2.6f,
                        HitKey = "hit_pierce", SfxKey = "sfx_achilles_pierce" },
                // 人：十二试炼——反打走普攻逻辑
                new() { SkillOrStatusId = "heracles_trials", Template = PerformanceTemplate.Melee,
                        SfxKey = "sfx_trials_counter" },
                // 反击类统一走普攻近身动画（与试炼/圣盾反制口径一致）：
                // 人：狮皮反击 / 冥：守门恶犬 / 海：漩涡巨口
                new() { SkillOrStatusId = "lion_counter", Template = PerformanceTemplate.Melee,
                        SfxKey = "sfx_lion_counter" },
                new() { SkillOrStatusId = "cerberus_guard", Template = PerformanceTemplate.Melee,
                        SfxKey = "sfx_cerberus_counter" },
                new() { SkillOrStatusId = "charybdis_maw", Template = PerformanceTemplate.Melee,
                        SfxKey = "sfx_charybdis_counter" },
                // 人：木马奇谋——炸弹专属图标；爆炸中缝裂开+专属音效
                new() { SkillOrStatusId = "trojan_bomb", Template = PerformanceTemplate.StatusTrigger,
                        ExtraIconKey = "icon_trojan_bomb", ExtraIconScale = 1.6f,
                        HitKey = "hit_explosion_crack", SfxKey = "sfx_trojan_explosion" },
                // 人：神器三借——飞剑专属图标弹道+专属伤害音效
                new() { SkillOrStatusId = "perseus_relics", Template = PerformanceTemplate.Auto,
                        ProjectileKey = "proj_flying_sword", HitKey = "hit_sword",
                        SfxKey = "sfx_perseus_swords" },
                // 海：海神三叉戟——我方棋盘呼吸海洋弱滤镜
                new() { SkillOrStatusId = "poseidon_oracle", Template = PerformanceTemplate.OracleAura,
                        BoardFilterKey = "filter_ocean", Intensity = 0.5f,
                        SfxKey = "sfx_oracle_poseidon" },
                new() { SkillOrStatusId = "poseidon_tide", Template = PerformanceTemplate.StatusTrigger,
                        ProjectileKey = "proj_wave", HitKey = "hit_wave", SfxKey = "sfx_trident_quake" },
                // 冥：冥域君临——我方棋盘冥域弱滤镜（复用海洋滤镜模板，换资源 key）
                new() { SkillOrStatusId = "hades_underworld_dominion", Template = PerformanceTemplate.OracleAura,
                        BoardFilterKey = "filter_underworld", Intensity = 0.5f,
                        SfxKey = "sfx_oracle_hades" },
                // 冥：石化凝视——石化边框/渐变/石头脱落音效在 UnitView + status 表现层，
                //     这里配触发反噬的演出与音效
                new() { SkillOrStatusId = "medusa_gaze", Template = PerformanceTemplate.StatusTrigger,
                        HitKey = "hit_petrify", SfxKey = "sfx_medusa_gaze" },
                new() { SkillOrStatusId = "petrify", Template = PerformanceTemplate.None,
                        SfxKey = "sfx_petrify_on", HitSfxKey = "sfx_petrify_off" },
            };
            return db;
        }
    }
}
