using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】CameraShaker：静态 Shake(strength, duration)。
    //
    // trauma 模型（2026-07-10 重写）：每次 Shake 往 trauma 累加，LateUpdate 按
    // trauma 大小施加 Perlin 噪声偏移并随时间衰减。相比旧的 DOShakePosition：
    //   - 连续命中天然叠加，绝无"上一次抖动瞬间完成→相机瞬移"的跳帧观感；
    //   - trauma 封顶 1，密集结算时相机偏移有上界；
    //   - 衰减到 0 自动精确回到基准位，不漂移。
    // 升级点：接入 Cinemachine 3 时把 Shake 内部替换为 Impulse，调用方式不变。
    // =========================================================================

    public static class CameraShaker
    {
        const float MaxOffset = 0.3f;   // trauma=1 时的最大偏移（世界单位）
        const float NoiseSpeed = 23f;   // 噪声频率：越大越"高频颤"

        static ShakeDriver _driver;

        public static void Shake(float strength = 0.15f, float duration = 0.25f)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (_driver == null || _driver.gameObject != cam.gameObject)
                _driver = cam.GetComponent<ShakeDriver>() ?? cam.gameObject.AddComponent<ShakeDriver>();
            _driver.AddTrauma(strength, duration);
        }

        /// <summary>跳过/快进时立刻停抖并复位。</summary>
        public static void Cancel()
        {
            // 不能用 ?.：重播重建相机后旧 driver 是"已销毁的假 null"，
            // ?. 会绕过 Unity 重载判空直接访问导致 MissingReferenceException
            if (_driver != null) _driver.Reset();
            else _driver = null; // 丢掉已销毁引用，下次 Shake 重挂
        }

        class ShakeDriver : MonoBehaviour
        {
            float _trauma;       // 0~1
            float _decayPerSec;  // 衰减速度（由 duration 推出）
            Vector3 _basePos;
            bool _hasBase;

            public void AddTrauma(float strength, float duration)
            {
                CaptureBase();
                // 旧接口 strength≈世界偏移量：除以 MaxOffset 折算成 trauma 增量
                _trauma = Mathf.Clamp01(_trauma + strength / MaxOffset);
                _decayPerSec = Mathf.Max(_decayPerSec, _trauma / Mathf.Max(0.05f, duration));
            }

            public void Reset()
            {
                _trauma = 0f;
                _decayPerSec = 0f;
                if (_hasBase) transform.localPosition = _basePos;
            }

            void CaptureBase()
            {
                if (_hasBase) return;
                _basePos = transform.localPosition;
                _hasBase = true;
            }

            void LateUpdate()
            {
                if (_trauma <= 0f) return;
                _trauma = Mathf.Max(0f, _trauma - _decayPerSec * Time.unscaledDeltaTime);
                float amp = _trauma * MaxOffset;
                float t = Time.unscaledTime * NoiseSpeed;
                var offset = new Vector3(
                    (Mathf.PerlinNoise(t, 1.3f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(1.7f, t) - 0.5f) * 2f, 0f) * amp;
                transform.localPosition = _basePos + offset;
                if (_trauma <= 0f) transform.localPosition = _basePos;
            }
        }
    }
}
