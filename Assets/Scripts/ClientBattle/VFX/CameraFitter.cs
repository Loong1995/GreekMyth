using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】相机自适配：不同机型分辨率/宽高比兼容的唯一权威。
    //
    // 设计安全区（半宽 DesignHalfWidth × 半高 DesignHalfHeight）固定；
    // - 正交：调 orthographicSize
    // - 透视默认（PerspectivePilot）：卡牌与地面夹角 CardLeanDeg → 卡姿 CardPitchDeg
    //   → 相机俯角 PilotPitchDeg（＝CardPitchDeg，光轴垂直卡面），见下方角度链注释
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
        // 【三个角，一条链】(2026-07-25 定稿：卡后倾 30° / 相机 ⟂ 卡面)
        //
        //   CardPitchDeg = 卡牌**后倾角** = Euler X = 与**竖直**方向的夹角（唯一真源）
        //   CardLeanDeg  = 卡牌与**地面**的夹角 = 90 − CardPitchDeg（派生，仅供换算）
        //   PilotPitchDeg= 相机俯角。定为 ＝ CardPitchDeg，即**光轴垂直于卡面**
        //
        // 这两个量历史上混用过（文档写"夹角=俯角 55°"其实是 35°）。定论：
        // "后倾 θ 度"一律指**离竖直** θ 度，实现就是 Euler(θ)。
        //
        // 【为什么 30 而不是 60】曾按"与地面 30°"实现（Euler 60 + 俯角 60）。
        // 那等于把卡摊在桌上从高处俯看，两个后果：影子纵深 3.13 比卡宽 2.04 还长，
        // 定位圆被撑到 1.8 倍卡宽，读作"圆被相机拉歪了"；竖直立件（罩身壳 8.7 米高）
        // 在陡俯角下透视收敛极强，读作"柱子指着相机"。改回 30 后影子纵深降到 1.81、
        // 定位圆 2.73（1.34 倍卡宽），机位也接近厂包预览的近平视，立件才立得住。
        //
        // 【为什么让相机垂直于卡面】卡面平行于成像平面时，透视对它只做等比缩放，
        // 立绘不被斜切 —— 卡面观感最干净。代价是卡牌本身没有左右梯形畸变，
        // 而躺在地面的圆必然是椭圆；二者的观感差异**不靠歪卡牌来抹平**，而是靠
        // 把「定位圆」定义成**卡牌影子（竖直投影）的外接圆**（见 ArenaSlotLayout），
        // 让圆的大小与位置天然贴合卡牌的地面足迹。
        //
        // 红线：卡牌姿态唯一真源是 CardPitchDeg。禁止任何地方再写 cam.eulerAngles.x
        // 当卡牌倾角用（那会让"调相机连带改卡姿"，且绕过这里的角度链）。

        /// <summary>卡牌后倾角（度）＝ Euler X ＝ 与竖直方向的夹角。
        /// 2026-07-25 定稿：45（与地面夹角 45）。当日试过 30（影子浅但立绘偏正面）
        /// 与"与地面 30°"＝Euler 60（影子过深，定位圆被撑到 1.8 倍卡宽）。</summary>
        public const float CardPitchDeg = 45f;

        /// <summary>卡牌与地面夹角（度）。派生量，供文档与几何换算引用。</summary>
        public const float CardLeanDeg = 90f - CardPitchDeg;

        /// <summary>相机俯角（度）＝ CardPitchDeg，使光轴垂直于卡面。</summary>
        public const float PilotPitchDeg = CardPitchDeg;

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

            // 相机保持正面 45° 俯视；「桌面扭转」由卡牌自转+站位绕圆心旋转体现
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

