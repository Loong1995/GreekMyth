using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】相机自适配：不同机型分辨率/宽高比兼容的唯一权威。
    //
    // 设计安全区（半宽 DesignHalfWidth × 半高 DesignHalfHeight）固定；
    // - 正交：调 orthographicSize
    // - 透视默认（PerspectivePilot）：卡姿 CardPitchDeg 与相机俯角 PilotPitchDeg
    //   **各自独立**（卡几何真源在本类；俯角数值在 StagePerformanceConfig）
    // 所有表现代码不得写死 orthoSize/FOV——统一依赖本组件。
    // =========================================================================

    [RequireComponent(typeof(Camera))]
    public class CameraFitter : MonoBehaviour
    {
        /// <summary>设计安全区半宽（世界单位，唯一真源；BattlefieldLayout 同源引用）。</summary>
        public const float SafeHalfWidth = 4.6f;
        /// <summary>设计安全区半高（世界单位，唯一真源）。</summary>
        public const float SafeHalfHeight = 5.2f;

        [Tooltip("设计安全区半宽（世界单位）：与 StanceLayout 三列区域对齐")]
        public float DesignHalfWidth = SafeHalfWidth;
        [Tooltip("设计安全区半高（世界单位）：覆盖前后排 |y|=3.65 + 卡半高 + 气泡余量")]
        public float DesignHalfHeight = SafeHalfHeight;

        /// <summary>透视默认（近 3D 舞台）。关则回正交。</summary>
        public static bool PerspectivePilot = true;

        // ------------------------------------------------------------ 卡牌与相机角度
        //
        // 【两个独立旋钮】卡后倾在本类；相机俯角在 StagePerformanceConfig.PilotPitchDeg
        //
        //   CardPitchDeg = 卡牌**后倾角** = Euler X = 与**竖直**方向的夹角
        //                  （站位/影子/定位圆几何的唯一真源）
        //   CardLeanDeg  = 卡牌与**地面**的夹角 = 90 − CardPitchDeg（派生）
        //   PilotPitchDeg= 相机俯角 = Euler X（从水平往下压；越大越俯视）
        //                  → 转发 StagePerformanceConfig，改数字即调参
        //
        // 术语定论："后倾 θ 度"一律指**离竖直** θ 度，实现就是 Euler(θ)。
        //
        // 【为什么解耦】此前 PilotPitchDeg ≡ CardPitchDeg，光轴垂直卡面。解耦后
        // 可单独调机位而不动卡几何。卡 Euler 60 曾试废（影子过长、罩身件收敛）。
        //
        // 红线：卡牌姿态唯一真源是 CardPitchDeg。禁止任何地方再写 cam.eulerAngles.x
        // 当卡牌倾角用（那会让"调相机连带改卡姿"）。

        /// <summary>卡牌后倾角（度）＝ Euler X ＝ 与竖直方向的夹角。
        /// 2026-07-25 定稿：45（与地面夹角 45）。几何（落点/影子/定位圆）
        /// 一律读此值，**不**跟相机俯角走。</summary>
        public const float CardPitchDeg = 45f;

        /// <summary>卡牌与地面夹角（度）。派生量，供文档与几何换算引用。</summary>
        public const float CardLeanDeg = 90f - CardPitchDeg;

        /// <summary>相机俯角（度）。数值权威在
        /// <see cref="StagePerformanceConfig.PilotPitchDeg"/>；本属性只转发，
        /// 调用方继续写 <c>CameraFitter.PilotPitchDeg</c> 即可。</summary>
        public static float PilotPitchDeg => StagePerformanceConfig.PilotPitchDeg;

        /// <summary>桌面扭转角（度，俯视顺时针）。相机保持正面（真转相机会让
        /// 地台远边一头高一头低+露黑角）；由卡牌自转+站位绕圆心旋转来体现
        /// ——圆形竞技场旋转不变，观感=坐在屏幕前微微侧身看卡。
        /// 战斗逻辑圆与卡牌同转此角（卡如固定 45° 贴在逻辑圆上）。
        /// 2026-07-25 定稿：0 = 不倾斜（8° 试后取消）。</summary>
        public const float PilotYawDeg = 0f;

        /// <summary>装饰大圆圆心 z（历史「逻辑圆」支点；站位分区已改为
        /// BattlefieldLayout 动态地面矩形，本值仅供贴图装饰圆参考）。</summary>
        public const float PilotPivotZ = 1.5f;

        /// <summary>地面平面高度（ArenaStageView.GroundY 同源引用此值）。</summary>
        public const float PilotGroundY = -5.2f;

        /// <summary>相机到棋盘中心（约原点）的距离（世界单位）。FOV 由本值与安全区
        /// 反算，所以这就是**镜头焦段**旋钮：拉远＝长焦＝更接近平行投影。
        ///
        /// 2026-07-25：12.5（FOV 49°）→ **30（FOV 20°）**。广角下离轴的竖直物体
        /// （罩身壳、卡牌）明显向两侧外倾、近远排大小差大，读作"两侧的特效歪了"；
        /// 长焦收敛离轴畸变，一台相机就能拿到"一排相机"的观感。
        /// 2026-07-26 定为 **55（极长焦）**：更接近平行投影。
        /// 代价是相机更远更高、屏底那条视线落在地面板近缘之外 → 露"桌沿"黑框；
        /// 解法不是缩回焦段，而是让 ArenaStageView 按视锥把地面板拉大到盖住
        /// （见该类 FitToCamera：近缘 z 每帧按屏底射线求交反算，不再吃死常量）。</summary>
        public const float PilotDistance = 55f;

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

        /// <summary>给定宽高比下 Pilot 相机的 FOV（与 ApplyPerspectivePilot 同式，
        /// 唯一定义源；BattlefieldLayout 用它做无相机对象的解析取景）。</summary>
        public static float PilotFovFor(float aspect)
        {
            if (aspect <= 0f) aspect = 16f / 9f;
            float fovForH = 2f * Mathf.Atan(SafeHalfHeight / PilotDistance) * Mathf.Rad2Deg;
            float fovForW = 2f * Mathf.Atan(SafeHalfWidth / (PilotDistance * aspect)) * Mathf.Rad2Deg;
            // 下限只防除零级的病态值，**不许**再钳回广角（2026-07-26 实测坑，见旧注释）
            return Mathf.Clamp(Mathf.Max(fovForH, fovForW) * 1.08f, 4f, 75f);
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

            // 俯角改了分区几何也要重算（缓存只按宽高比键，否则调参不进 Play 也看不见）
            BattlefieldLayout.InvalidateCache();

            // 相机保持正面俯视（俯角 = PilotPitchDeg）；「桌面扭转」由卡牌自转+站位旋转体现
            float pitchRad = PilotPitchDeg * Mathf.Deg2Rad;
            _cam.transform.position = new Vector3(
                0f,
                Mathf.Sin(pitchRad) * PilotDistance,
                -Mathf.Cos(pitchRad) * PilotDistance);
            _cam.transform.rotation = Quaternion.Euler(PilotPitchDeg, 0f, 0f);

            _cam.fieldOfView = PilotFovFor(aspect);
        }
    }
}

