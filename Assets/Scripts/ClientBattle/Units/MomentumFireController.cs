using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第5层 单位表现】MomentumFireController：势能火 + 卡后金光环唯一生命周期。
    //
    // 分档：四轨最高值 <4 无 / ≥4 小 / ≥5 / ≥6 / ≥7 满分大。
    // 火与金光环同档同灭：Refresh 同挂、Fade 同渐灭、Extinguish/Clear 同撤。
    // 相位信号见 PlaybackDirector（ActionPause / 回合横幅）。
    // hold-off：渐灭后抑制同值重挂；值变化立即解除（见历史 g1r5 修复）。
    // =========================================================================

    public class MomentumFireController
    {
        readonly Transform _mount;
        GameObject _fire;
        GameObject _glow;
        int _tier;              // 0=无，1..4 对应 4/5/6/7+
        Tween _fade;
        bool _heldOff;
        int _heldOffValue = -1;

        // 火：卡上缘；光环：卡后 LightGlow A（无星点）
        static readonly float[] FireScales = { 0f, 0.32f, 0.48f, 0.68f, 0.92f };
        static readonly float[] GlowScales = { 0f, 1.18f, 1.32f, 1.48f, 1.65f };
        int _lastMaxValue;

        public MomentumFireController(Transform mount) => _mount = mount;

        /// <summary>按四轨最高势能挂/调火与金光环。值变化会解除渐灭抑制。</summary>
        public void Refresh(int maxTrackValue)
        {
            if (_heldOff)
            {
                if (maxTrackValue == _heldOffValue) return;
                _heldOff = false;
            }
            _lastMaxValue = maxTrackValue;
            int tier = TierOf(maxTrackValue);
            if (tier == _tier && (tier == 0 || (_fire != null && _glow != null))) return;
            KillFade();
            _tier = tier;
            DestroyFx(ref _fire);
            DestroyFx(ref _glow);
            if (tier == 0) return;
            _fire = UnitAuraService.MountMomentumFire(_mount, FireScales[tier]);
            _glow = UnitAuraService.MountMomentumGlow(_mount, GlowScales[tier]);
        }

        /// <summary>行动切换停顿：火+金光环同渐灭（停发粒子 + 缩放归零）。</summary>
        public void Fade(float duration)
        {
            _heldOff = true;
            _heldOffValue = _lastMaxValue;
            if (_fire == null && _glow == null)
            {
                _tier = 0;
                return;
            }
            KillFade();
            float d = Mathf.Max(0.05f, duration);
            var seq = DOTween.Sequence().OnComplete(Extinguish);
            FadeRoot(seq, _fire, d);
            FadeRoot(seq, _glow, d);
            _fade = seq;
        }

        static void FadeRoot(Sequence seq, GameObject root, float duration)
        {
            if (root == null) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var emission = ps.emission;
                emission.enabled = false;
            }
            seq.Join(root.transform.DOScale(Vector3.zero, duration).SetEase(Ease.InQuad));
        }

        /// <summary>立即撤火与金光环（停顿收尾 / 清账）；不改四轨条与 hold-off。</summary>
        public void Extinguish()
        {
            KillFade();
            _tier = 0;
            DestroyFx(ref _fire);
            DestroyFx(ref _glow);
        }

        /// <summary>自身行动窗清账：撤火/光环并完全复位。</summary>
        public void Clear()
        {
            _heldOff = false;
            _heldOffValue = -1;
            _lastMaxValue = 0;
            Extinguish();
        }

        static int TierOf(int v) =>
            v >= 7 ? 4 : v >= 6 ? 3 : v >= 5 ? 2 : v >= 4 ? 1 : 0;

        static void DestroyFx(ref GameObject fx)
        {
            if (fx == null) return;
            Object.Destroy(fx);
            fx = null;
        }

        void KillFade()
        {
            if (_fade != null && _fade.IsActive()) _fade.Kill();
            _fade = null;
        }

        // ------------------------------------------------------------ 相位信号（棋盘级）

        public static void OnActionPauseBegin(BattleBoardView board, float fadeDuration)
        {
            if (board == null) return;
            foreach (var u in board.AllUnits) u?.MomentumFire.Fade(fadeDuration);
        }

        public static void OnActionPauseEnd(BattleBoardView board)
        {
            if (board == null) return;
            foreach (var u in board.AllUnits) u?.MomentumFire.Extinguish();
        }

        public static void OnRoundBanner(BattleBoardView board, float fadeDuration) =>
            OnActionPauseBegin(board, fadeDuration);
    }
}
