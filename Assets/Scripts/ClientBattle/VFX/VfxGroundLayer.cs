using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>标记「这是地面层特效」，VFXManager 出池时不得把它的粒子排序
    /// 抬到空中特效档（下限 45）——地面裂地/尘雾必须留在卡牌之下，
    /// 否则会糊在英雄立绘前面。挂在裂地 prefab 根上（GroundCrackComposer 写入）。</summary>
    public class VfxGroundLayer : MonoBehaviour
    {
    }
}
