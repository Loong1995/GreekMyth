using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>标记：本件**每次播都新建实例、播完销毁**，不进对象池。
    ///
    /// 【为什么需要】厂包特效（RFX 系列等）的观感有相当一部分不是粒子自己跑的，
    /// 而是它自带的驱动脚本在 `Awake/Start` 里初始化、再逐帧驱动曲线/灯光/子物体启停。
    /// 而对象池的复用只是 `SetActive(true)`——**Unity 不会重跑 Awake/Start**。
    /// 更要命的是 `Prewarm()` 开局就把每个 prefab 实例化并入池，于是战斗里
    /// **第一次播就已经是复用态**，那套初始化整局只在离屏预热区跑过一次。
    /// 结果：粒子还动（我们手动重启了），脚本驱动的层全停在残留状态——
    /// 就是"画廊里挺好、战斗里差一大截"的主因之一。
    ///
    /// 【代价】每次播多一次 Instantiate（粒子层级约 1~3 ms）。所以这是**白名单**，
    /// 由接线脚本按"含 RFX* 驱动脚本"自动判定，不要手动往常用小件上挂。
    /// 高频件（命中/受击）宁可保持池化，也不要用这个标记。
    ///
    /// 【必须独立成文件】本组件要被**序列化进 prefab**。Unity 只按「类名＝文件名」
    /// 解析 prefab 里的 MonoBehaviour 引用，塞在 VFXManager.cs 里时组件能
    /// AddComponent、却在存盘后变成 missing script（2026-07-27 实翻过车：
    /// 标记静默丢失 → 绕池逻辑整个失效，且只有一行不起眼的 missing 警告）。</summary>
    public class VfxFreshInstance : MonoBehaviour
    {
    }
}
