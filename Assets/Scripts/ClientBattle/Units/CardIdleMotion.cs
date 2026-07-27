using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【卡面生动性合成器】立绘的三条动态通道合成为一组 localPosition / localScale /
    // localRotation，由 UnitView 每帧 Tick 一次写入。
    //
    //   1. 待机呼吸：上下浮 + 侧摆 + 胸腔缩放 + 微倾，三个互质频率叠加，
    //      每卡相位与频率各自失谐 —— 六张卡同屏永不同步（同步即读作机关）。
    //      兵力越低呼吸越慢越重（免费的叙事：残血的人喘得厉害）。
    //   2. 惯性视差：立绘比卡框「慢半拍」。卡根位移（冲锋/受击抖动）时立绘先滞后
    //      再弹回，配合立绘本身的景深偏移，读作卡框里装着一个有重量的人，
    //      而不是一张贴纸。
    //   3. 受击挤压：命中瞬间沿受击方向的弹性挤压+回弹（阻尼正弦）。
    //
    // 为什么不用 DOTween：三条通道同时作用在同一个 Transform 上，tween 之间
    // 互相 Kill 会让呼吸断掉、或让立绘停在挤压到一半的姿态上。合成器只有一份
    // 状态、一处写入，天然互斥安全，且 Tick 内零 alloc。
    //
    // 文档：docs/client/performance_mechanisms.md（卡面生动性）
    // =========================================================================

    sealed class CardIdleMotion
    {
        // ---- 待机呼吸（振幅单位＝LayoutScale 倍的世界单位；频率＝弧度/秒）----
        const float BobAmp = 0.035f, BobFreq = 2.10f;
        const float SwayAmp = 0.014f, SwayFreq = 1.37f;
        const float TiltAmpDeg = 0.7f, TiltFreq = 0.83f;
        const float BreathScale = 0.014f;

        // ---- 惯性视差（三个值是观感调参主旋钮，需真机目视校准，先取保守值）----
        /// <summary>卡根位移有多少比例转成立绘滞后（0＝立绘钉死在框上）。</summary>
        const float LagGain = 0.35f;
        /// <summary>滞后回弹速度（每秒衰减率）。越大越"硬"，越小越"果冻"。</summary>
        const float LagRecover = 11f;
        /// <summary>滞后位移上限（×LayoutScale）。必须显著小于内窗留白，
        /// 否则受击抖动（DOShakePosition 20 次高频往返）会把立绘甩出卡框。</summary>
        const float LagMax = 0.09f;

        // ---- 受击挤压 ----
        const float PunchDecay = 4.2f;    // 1→0 约 0.24s
        const float PunchSquash = 0.16f;  // 峰值形变比例
        const float PunchShove = 0.07f;   // 峰值位移（×LayoutScale）
        const float PunchTiltDeg = 3.5f;

        Transform _portrait;
        Vector3 _baseLocalPos;
        Vector3 _baseLocalScale;
        float _amp = 1f;      // ＝LayoutScale
        float _phase;         // 每卡错开
        float _detune = 1f;   // 每卡频率失谐

        bool _frozen;
        bool _hasLastRoot;
        Vector3 _lastRoot;
        Vector3 _lagLocal;

        float _punch;         // 1→0
        Vector2 _punchDir;

        /// <summary>绑定/重绑立绘（建卡与整局重置各调一次）。
        /// 必须在 FitSpriteToSlot 与 localPosition 初值都写完之后调用——
        /// 本类把当时的 pos/scale 记作基准，之后每帧都相对它合成。</summary>
        public void Bind(Transform portrait, float layoutScale, float phaseSeed)
        {
            _portrait = portrait;
            if (_portrait == null) return;
            _baseLocalPos = _portrait.localPosition;
            _baseLocalScale = _portrait.localScale;
            _amp = Mathf.Max(0.1f, layoutScale);
            _phase = phaseSeed;
            // 失谐 ±12%：六张卡的呼吸周期互不整除，看多久都不会「一起吸气」
            _detune = 1f + 0.12f * Mathf.Sin(phaseSeed * 1.7f);
            _frozen = false;
            _hasLastRoot = false;
            _lagLocal = Vector3.zero;
            _punch = 0f;
            _punchDir = Vector2.zero;
        }

        /// <summary>石化/阵亡：冻成静止像并把立绘贴回基准姿态。</summary>
        public void SetFrozen(bool frozen)
        {
            _frozen = frozen;
            if (!frozen || _portrait == null) return;
            _lagLocal = Vector3.zero;
            _punch = 0f;
            _portrait.localPosition = _baseLocalPos;
            _portrait.localScale = _baseLocalScale;
            _portrait.localRotation = Quaternion.identity;
        }

        /// <summary>受击挤压脉冲。strength 1＝暴击、0.6＝普通命中；
        /// shoveLocal＝受击方向（卡局部 xy，已归一化，可为零＝无方向）。
        /// 与卡根位移抖动是**两条独立通道**：有绕身罩禁位移时（P-58）挤压照给，
        /// 否则命中完全没有肉感。</summary>
        public void Punch(float strength, Vector2 shoveLocal)
        {
            if (_frozen) return;
            // 连击取较强的一发重置，不叠加——叠加会把立绘越挤越扁
            _punch = Mathf.Max(_punch, Mathf.Clamp01(strength));
            _punchDir = shoveLocal;
        }

        /// <summary>每帧合成写入。root＝卡根（读其世界位移算惯性）；
        /// troopsRatio＝兵力比（越低呼吸越慢越重）。</summary>
        public void Tick(Transform root, float dt, float troopsRatio)
        {
            if (_portrait == null) return;
            if (_frozen)
            {
                _hasLastRoot = false; // 解冻后不要把冻结期间的位移一次性抖出来
                return;
            }

            TrackInertia(root, dt);

            // 残血：频率降到 0.72 倍、振幅升到 1.35 倍（喘得慢而深）
            float weak = 1f - Mathf.Clamp01(troopsRatio);
            float freqMul = _detune * Mathf.Lerp(1f, 0.72f, weak);
            float ampMul = _amp * Mathf.Lerp(1f, 1.35f, weak);

            float t = Time.time;
            float breath = Mathf.Sin(t * BobFreq * freqMul + _phase);
            float bob = breath * BobAmp * ampMul;
            float sway = Mathf.Sin(t * SwayFreq * freqMul + _phase * 1.7f) * SwayAmp * ampMul;
            float tilt = Mathf.Sin(t * TiltFreq * freqMul + _phase * 0.6f) * TiltAmpDeg;

            // 阻尼正弦：挤压 → 过冲拉伸 → 收敛，比线性衰减更像被打了一下
            _punch = Mathf.Max(0f, _punch - dt * PunchDecay);
            float wobble = _punch > 0f
                ? Mathf.Sin(_punch * Mathf.PI * 3f) * _punch
                : 0f;

            // 胸腔起伏：宽略收、高略涨（等体积感），再叠受击挤压（相位相反）
            float sx = 1f + breath * -BreathScale * 0.6f + wobble * PunchSquash;
            float sy = 1f + breath * BreathScale + wobble * -PunchSquash;

            float shove = wobble * PunchShove * _amp;
            _portrait.localPosition = new Vector3(
                _baseLocalPos.x + sway + _lagLocal.x + _punchDir.x * shove,
                _baseLocalPos.y + bob + _lagLocal.y + _punchDir.y * shove,
                _baseLocalPos.z + _lagLocal.z);
            _portrait.localScale = new Vector3(
                _baseLocalScale.x * sx, _baseLocalScale.y * sy, _baseLocalScale.z);
            _portrait.localRotation = Quaternion.Euler(
                0f, 0f, tilt + wobble * PunchTiltDeg * -_punchDir.x);
        }

        /// <summary>卡根这一帧走了多远 → 立绘反向滞后一点，然后指数回弹到零。
        /// 位移换算到卡局部坐标：卡牌在近 3D 下有 45° 后倾，用世界向量会把
        /// 前后位移错算成上下浮动。</summary>
        void TrackInertia(Transform root, float dt)
        {
            if (root == null) return;
            Vector3 now = root.position;
            if (_hasLastRoot)
            {
                Vector3 deltaLocal = root.InverseTransformVector(now - _lastRoot);
                _lagLocal -= deltaLocal * LagGain;
                float max = LagMax * _amp;
                if (_lagLocal.sqrMagnitude > max * max)
                    _lagLocal = _lagLocal.normalized * max;
            }
            _lastRoot = now;
            _hasLastRoot = true;
            _lagLocal = Vector3.Lerp(_lagLocal, Vector3.zero, Mathf.Clamp01(dt * LagRecover));
        }
    }
}
