using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】相机自适配：不同机型分辨率/宽高比兼容的唯一权威。
    //
    // 设计安全区（半宽 DesignHalfWidth × 半高 DesignHalfHeight）固定；
    // - 正交：调 orthographicSize
    // - 透视默认（PerspectivePilot）：俯角 PilotPitchDeg，卡 billboard 后与地面夹角≈俯角
    // 所有表现代码不得写死 orthoSize/FOV——统一依赖本组件。
    // =========================================================================

    [RequireComponent(typeof(Camera))]
    public class CameraFitter : MonoBehaviour
    {
        [Tooltip("设计安全区半宽（世界单位）：与 StanceLayout 三列区域对齐")]
        public float DesignHalfWidth = 4.6f;
        [Tooltip("设计安全区半高（世界单位）：覆盖前后排 |y|=3.65 + 卡半高 + 气泡余量")]
        public float DesignHalfHeight = 5.2f;

        /// <summary>透视默认：卡与地面夹角≈俯角（见 PilotPitchDeg）。关则回正交。</summary>
        public static bool PerspectivePilot = true;

        /// <summary>近 3D 默认俯角（度）。卡 FaceCamera 后与水平地面夹角≈此值。</summary>
        public const float PilotPitchDeg = 45f;

        /// <summary>相机到棋盘中心（约原点）的距离（世界单位），用于 45° 取景。</summary>
        public const float PilotDistance = 12.5f;

        Camera _cam;
        int _lastW, _lastH;

        public static CameraFitter EnsureOn(Camera cam)
        {
            if (cam == null) return null;
            var fitter = cam.GetComponent<CameraFitter>();
            if (fitter == null) fitter = cam.gameObject.AddComponent<CameraFitter>();
            fitter.Fit();
            return fitter;
        }

        /// <summary>在给定世界深度平面上，当前相机可见的半高（世界单位）。</summary>
        public static float VisibleHalfHeightAt(Camera cam, float worldZ)
        {
            if (cam == null) return 5.2f;
            if (cam.orthographic) return cam.orthographicSize;
            float dist = Mathf.Abs(cam.transform.position.z - worldZ);
            if (dist < 0.01f) dist = 0.01f;
            return dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        public static float VisibleHalfWidthAt(Camera cam, float worldZ)
            => VisibleHalfHeightAt(cam, worldZ) * (cam != null ? cam.aspect : 1f);

        void Awake() => _cam = GetComponent<Camera>();

        void LateUpdate()
        {
            if (Screen.width != _lastW || Screen.height != _lastH) Fit();
        }

        public void Fit()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            _lastW = Screen.width;
            _lastH = Screen.height;
            if (_lastH <= 0) return;

            float aspect = (float)_lastW / _lastH;

            if (PerspectivePilot)
            {
                ApplyPerspectivePilot(aspect);
                return;
            }

            if (!_cam.orthographic) return;
            _cam.orthographicSize = Mathf.Max(DesignHalfHeight, DesignHalfWidth / aspect);
        }

        void ApplyPerspectivePilot(float aspect)
        {
            _cam.orthographic = false;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 80f;
            _cam.allowHDR = true;

            // 俯角 = 卡-地夹角；相机沿视线置于棋盘中心正后方斜上方
            float pitchRad = PilotPitchDeg * Mathf.Deg2Rad;
            _cam.transform.position = new Vector3(
                0f,
                Mathf.Sin(pitchRad) * PilotDistance,
                -Mathf.Cos(pitchRad) * PilotDistance);
            _cam.transform.rotation = Quaternion.Euler(PilotPitchDeg, 0f, 0f);

            float dist = PilotDistance;
            float fovForH = 2f * Mathf.Atan(DesignHalfHeight / dist) * Mathf.Rad2Deg;
            float fovForW = 2f * Mathf.Atan(DesignHalfWidth / (dist * aspect)) * Mathf.Rad2Deg;
            _cam.fieldOfView = Mathf.Clamp(Mathf.Max(fovForH, fovForW) * 1.08f, 28f, 75f);
        }
    }
}
