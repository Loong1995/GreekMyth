using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】相机自适配：不同机型分辨率/宽高比兼容的唯一权威。
    //
    // 原理：世界坐标里的棋盘布局固定不变（设计安全区 = 半宽 DesignHalfWidth ×
    // 半高 DesignHalfHeight），本组件按当前屏幕宽高比动态调 orthographicSize，
    // 保证安全区在任意机型上完整可见：
    //   - 宽屏（平板/横屏）：高度撑满，两侧多出的世界空间由背景铺满；
    //   - 窄屏/竖屏（手机）：放大 orthoSize 保住宽度，上下多出空间同理。
    // 分辨率热切换（转屏/分屏/桌面拉窗口）每帧检测，变化即重新取景。
    // 所有表现代码不得直接假设屏幕像素或写死 orthoSize——统一依赖本组件。
    // =========================================================================

    [RequireComponent(typeof(Camera))]
    public class CameraFitter : MonoBehaviour
    {
        [Tooltip("设计安全区半宽（世界单位）：与 StanceLayout 三列区域对齐")]
        public float DesignHalfWidth = 4.6f;
        [Tooltip("设计安全区半高（世界单位）：覆盖前后排 |y|=3.65 + 卡半高 + 气泡余量")]
        public float DesignHalfHeight = 5.2f;

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
            if (!_cam.orthographic || _lastH <= 0) return;
            float aspect = (float)_lastW / _lastH;
            // 高度直接取半高；宽度不足时放大视野以保住半宽
            _cam.orthographicSize = Mathf.Max(DesignHalfHeight, DesignHalfWidth / aspect);
        }
    }
}
