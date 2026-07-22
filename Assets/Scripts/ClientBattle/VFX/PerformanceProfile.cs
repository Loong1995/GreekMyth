using System;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 演出配置数据（纯数据，供 VFXResolver 三级查找返回）：
    // 一条 profile 描述"这个战法/状态怎么演"——模板类别 + 各资源 key + 参数。
    // 所有 key 都走占位回退（VFXManager/SfxManager），资源未上传也必然能播。
    // =========================================================================

    /// <summary>演出模板（默认策略族，client_perform §一）。</summary>
    public enum PerformanceTemplate
    {
        Auto,           // 按事件形状自动挑：群攻→AoeCenter，单体→PerSegment
        AoeCenter,      // 群攻主动：施法者移动到棋盘中心 → N 道刀光/魔法光 → 掉血
        PerSegment,     // 非群攻主动：按伤害段数逐段播放
        Melee,          // 普攻/近身：移动到被打者卡牌近身，命中帧闪斩击（追击更大）
        StatusTrigger,  // 特殊状态触发：走主动逻辑，飘状态来源战法名
        RemoteStrike,   // 远程落击：施法者不位移；目标头顶头像标 + 自上而下命中特效（雷霆）
        OracleAura,     // 神谕：施加完所有单位后一次性挂特效（整战法一个播放单元）
        None,           // 无演出（如蛇杖庇护圣谕宣告）
    }

    [Serializable]
    public class PerformanceProfile
    {
        [Header("匹配")]
        public string SkillOrStatusId = "";           // 空 = 组默认
        public PerformanceTemplate Template = PerformanceTemplate.Auto;

        [Header("资源 key（Resources/ClientBattle/ 下同名覆盖生效）")]
        public string ProjectileKey = "";             // 弹道（飞行默认 blade_bolt/magic_bolt；近身斩击默认 slash）
        public string HitKey = "";                    // 命中特效
        public string CastKey = "";                   // 施法前摇特效
        public string AuraKey = "";                   // 常驻光环（神谕挂身/暴击机会等）
        public string BoardFilterKey = "";            // 整盘滤镜（海洋呼吸/血色呼吸）
        public string ExtraIconKey = "";              // 特殊图标（裂甲长矛/木马炸弹等）
        public string PortraitMarkKey = "";           // 头像标（B5 皇卡：受影响单位头顶
                                                      // 短暂浮现指定武将头像，如 zeus/hades）
        public string SfxKey = "";                    // 主音效
        public string HitSfxKey = "";                 // 命中音效

        [Header("参数（强度/缩放，后续人工调节）")]
        [Range(0f, 3f)] public float Intensity = 1f;  // 滤镜/光环强度参数
        public float ExtraIconScale = 1f;             // 特殊图标缩放（裂甲图标"比一般状态图标大很多"）
        public float StrikeVfxScale = 1f;             // 近身斩击缩放（普攻 1.0 基准、追击组默认更大）
        public bool CameraShakeOnHit = true;
        /// <summary>连发（BurstNo≥2）时的组内节拍加速倍率（B1）。</summary>
        public float BurstTempoScale = 1.35f;
        /// <summary>犹豫延迟宣告（kind=delayed）的「延迟」飘字停留秒数。</summary>
        public float DelayedAnnouncePause = 0.35f;
        /// <summary>借刀近战：Melee 时每段由 damage.SourceId 单位突进（帕特洛克勒斯代战），
        /// 而非组根施法者。</summary>
        public bool BorrowBlade = false;

        public PerformanceProfile Clone() => (PerformanceProfile)MemberwiseClone();
    }
}