using DigitalRuby.LightningBolt;
using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>竖雷闪烁：在持续时间内反复 <see cref="LightningBoltScript.Trigger"/>，
    /// 每拍重算折线 + 推进贴图行 —— 否则 ManualMode 只 Trigger 一次，整段是静止灰白线。</summary>
    sealed class DrLightningFlicker : MonoBehaviour
    {
        LightningBoltScript _bolt;
        float _left;
        float _interval;
        float _accum;

        public void Begin(LightningBoltScript bolt, float duration, float interval = 0.045f)
        {
            _bolt = bolt;
            _left = Mathf.Max(0.02f, duration);
            _interval = Mathf.Clamp(interval, 0.03f, 0.12f);
            _accum = 0f;
            enabled = true;
        }

        void Update()
        {
            if (_bolt == null) { Destroy(this); return; }
            float dt = Time.deltaTime;
            _left -= dt;
            if (_left <= 0f) { Destroy(this); return; }
            _accum += dt;
            if (_accum < _interval) return;
            _accum = 0f;
            _bolt.Trigger();
        }
    }
}
