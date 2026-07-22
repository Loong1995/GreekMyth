using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第5层 单位表现】MomentumFireController：势能火（CFXR3）唯一生命周期管理。
    //
    // 分档：四轨最高值 <4 无 / ≥4 小 / ≥5 / ≥6 / ≥7 满分大。
    // 生命周期：
    //   - Refresh(max)：momentum_change 落账驱动（MomentumService.Apply）。
    //   - 相位信号（Runner 只发信号，不逐单位管火）：
    //       OnActionPauseBegin  行动切换停顿 → 全场火渐灭（粒子停发+缩放归零）；
    //       OnActionPauseEnd    停顿结束 → 残余火强制销毁；
    //       OnRoundBanner       回合横幅前 → 提前开渐灭（末位行动→下回合无 ActionStart）。
    //   - Clear()：自身行动窗清账（ClearMomentum）时撤火并复位 hold-off。
    // hold-off 语义（2026-07-22 修正 g1r5 满势能无火）：渐灭只抑制"账本旧值
    // 重挂"——记录灭火时的档位值，之后任何**值发生变化**的 momentum_change
    // 立即解除抑制并按新档点火（借刀/响应在别人行动窗涨势能也能起火）。
    // =========================================================================

    public class MomentumFireController
    {
        readonly Transform _mount;
        GameObject _fire;
        int _tier;              // 0=无，1..4 对应 4/5/6/7+
        Tween _fade;
        bool _heldOff;          // 渐灭后抑制"同值重挂"
        int _heldOffValue = -1; // 灭火时账本的四轨最高值；值变化即解除抑制

        static readonly float[] Scales = { 0f, 0.32f, 0.48f, 0.68f, 0.92f };
        int _lastMaxValue; // 最近一次 Refresh 的权威最高值（供渐灭时记录）

        public MomentumFireController(Transform mount) => _mount = mount;

        /// <summary>按四轨最高势能挂/调火。值变化会解除渐灭抑制（bug 修复点）。</summary>
        public void Refresh(int maxTrackValue)
        {
            if (_heldOff)
            {
                if (maxTrackValue == _heldOffValue) return; // 账本旧值：维持熄灭
                _heldOff = false;                           // 新值到账：解除抑制重新点火
            }
            _lastMaxValue = maxTrackValue;
            int tier = TierOf(maxTrackValue);
            if (tier == _tier && (tier == 0 || _fire != null)) return;
            KillFade();
            _tier = tier;
            if (_fire != null)
            {
                Object.Destroy(_fire);
                _fire = null;
            }
            if (tier == 0) return;
            _fire = UnitAuraService.MountMomentumFire(_mount, Scales[tier]);
        }

        /// <summary>行动切换停顿内火渐灭（粒子停发 + 缩放归零）；账本条不受影响。</summary>
        public void Fade(float duration)
        {
            _heldOff = true;
            _heldOffValue = _lastMaxValue;
            if (_fire == null)
            {
                _tier = 0;
                return;
            }
            KillFade();
            float d = Mathf.Max(0.05f, duration);
            var root = _fire;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var emission = ps.emission;
                emission.enabled = false;
            }
            _fade = root.transform.DOScale(Vector3.zero, d)
                .SetEase(Ease.InQuad)
                .OnComplete(Extinguish);
        }

        /// <summary>立即撤火（停顿收尾 / 清账）；不改四轨条与 hold-off。</summary>
        public void Extinguish()
        {
            KillFade();
            _tier = 0;
            if (_fire == null) return;
            Object.Destroy(_fire);
            _fire = null;
        }

        /// <summary>自身行动窗清账：撤火并完全复位（下窗从零重新累计）。</summary>
        public void Clear()
        {
            _heldOff = false;
            _heldOffValue = -1;
            _lastMaxValue = 0;
            Extinguish();
        }

        static int TierOf(int v) =>
            v >= 7 ? 4 : v >= 6 ? 3 : v >= 5 ? 2 : v >= 4 ? 1 : 0;

        void KillFade()
        {
            if (_fade != null && _fade.IsActive()) _fade.Kill();
            _fade = null;
        }

        // ------------------------------------------------------------ 相位信号（棋盘级）

        /// <summary>ActionPause 开始：全场火渐灭。</summary>
        public static void OnActionPauseBegin(BattleBoardView board, float fadeDuration)
        {
            if (board == null) return;
            foreach (var u in board.AllUnits) u?.MomentumFire.Fade(fadeDuration);
        }

        /// <summary>ActionPause 结束：全场残余火强制销毁。</summary>
        public static void OnActionPauseEnd(BattleBoardView board)
        {
            if (board == null) return;
            foreach (var u in board.AllUnits) u?.MomentumFire.Extinguish();
        }

        /// <summary>回合横幅前：提前开渐灭（末位行动→下回合之间往往没有 ActionStart）。</summary>
        public static void OnRoundBanner(BattleBoardView board, float fadeDuration) =>
            OnActionPauseBegin(board, fadeDuration);
    }
}
