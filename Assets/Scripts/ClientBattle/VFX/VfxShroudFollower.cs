using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 罩身运行期通用驱动（所有罩身件共用）：
    //   · 平时 / melee：整件钉在持有者卡牌投影圆心（ProjectionCircleCenter），世界竖直；
    //   · 地面 Decal 在 Fit 时已严格＝投影圆直径，跟随只改位姿不重乘 Pin；
    //   · 粒子强制 Local，melee 时不甩在身后。
    // **禁止在本类裁层/删节点**：厂包同构件默认完整加载；去石块/关 Trigger 等
    // 一律由各技能挂载处单独名单配置（见 MountShroud / Wire）。
    // 定径仍走 VfxShroudFitter.Fit。
    // =========================================================================

    public sealed class VfxShroudFollower : MonoBehaviour
    {
        const int MinSortingOrder = 45;

        UnitView _unit;
        Transform _cell;
        Vector3 _fitScale = Vector3.one;

        /// <summary>罩身实例（Fit 后的那棵）。</summary>
        public Transform Cell => _cell;

        /// <summary>锚定的单位。</summary>
        public UnitView Unit => _unit;

        /// <summary>
        /// 挂上跟随：先由调用方 <see cref="VfxShroudFitter.Fit"/>，再本方法接管位姿。
        /// cell 脱到世界空间（不受卡牌后倾连带）；lifetimeOwner 销毁时顺带销毁 cell。
        /// </summary>
        public static VfxShroudFollower Attach(UnitView unit, GameObject cell,
                                              Component lifetimeOwner)
        {
            if (cell == null) return null;
            var host = lifetimeOwner != null ? lifetimeOwner.gameObject : cell;
            var follower = host.GetComponent<VfxShroudFollower>()
                           ?? host.AddComponent<VfxShroudFollower>();
            follower.Bind(unit, cell);
            return follower;
        }

        /// <summary>Fit + 排序抬升 + ForcePlay + 跟随，一站式（战斗挂载入口）。</summary>
        public static VfxShroudFollower FitAndFollow(UnitView unit, GameObject cell,
                                                    Component lifetimeOwner)
        {
            if (cell == null) return null;
            var anchor = unit != null ? unit.transform.position : cell.transform.position;
            VfxShroudFitter.Fit(cell, anchor);
            PrepareRuntime(cell);
            return Attach(unit, cell, lifetimeOwner);
        }

        /// <summary>与画廊一致的起播与排序；粒子改 Local 以便跟随。</summary>
        public static void PrepareRuntime(GameObject cell)
        {
            if (cell == null) return;
            foreach (var ps in cell.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                ps.Clear(true);
                ps.Play(true);
            }
            foreach (var r in cell.GetComponentsInChildren<Renderer>(true))
                if (r.sortingOrder < MinSortingOrder) r.sortingOrder = MinSortingOrder;
        }

        void Bind(UnitView unit, GameObject cell)
        {
            _unit = unit;
            _cell = cell != null ? cell.transform : null;
            if (_cell == null) return;
            _fitScale = _cell.lossyScale;
            _cell.SetParent(null, true);
            Snap();
        }

        void LateUpdate() => Snap();

        /// <summary>钉投影圆：melee 时跟 transform，平时亦同（严格锚定投影圆）。</summary>
        public void Snap()
        {
            if (_cell == null) return;
            Vector3 cardAnchor = _unit != null
                ? _unit.transform.position
                : _cell.position;
            var center = ArenaSlotLayout.ProjectionCircleCenter(cardAnchor);
            _cell.SetPositionAndRotation(center, Quaternion.identity);
            _cell.localScale = _fitScale;
        }

        void OnDestroy()
        {
            if (_cell != null) Object.Destroy(_cell.gameObject);
        }
    }
}
