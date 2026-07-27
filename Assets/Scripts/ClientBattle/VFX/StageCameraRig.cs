using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】舞台推镜：在一段演出期间**临时接管相机位姿**，结束还位。
    //
    // 为什么要单独一层：`CameraFitter` 只在分辨率变化时摆一次相机（静态机位），
    // `CameraShaker` 只加一个抖动偏移。要做"镜头压过来"这种**演出性运镜**，
    // 既不能改 CameraFitter 的静态真源（那是全局几何基准，一改站位/影子全变），
    // 也不能各演出自己 `cam.transform.position = ...`（两处写相机必打架）。
    //
    // 【唯一写方】接管期间本组件是相机位姿的唯一写方：
    //   位姿 = 由 (俯角, 距离) 解析求出的基准位 ＋ CameraShaker.CurrentOffset
    // 抖动被切到"只算不写"（`CameraShaker.Suspended`），于是两个 LateUpdate 谁先
    // 谁后都不影响结果——否则会出现"抖一下不抖一下"的随机现象。
    //
    // 【只动俯角与距离，不动 FOV】FOV 是 CameraFitter 按安全区反算的取景基准，
    // 动它等于换镜头畸变。"显著变近"靠**缩短距离**实现：极长焦下距离一缩，
    // 主体就直接顶上来，透视关系不变（不会突然变广角脸）。
    //
    // 用法（协程里）：
    //     var rig = StageCameraRig.Ensure();
    //     rig.Blend(pitch, distance, p);   // p: 0=原机位 1=目标机位，每帧调
    //     ...
    //     rig.Release();                   // 必须调；中断路径也要调
    //
    // **谁接管谁归还**：`Release` 幂等，正常收尾与中断（CancelAll/HardStop）
    // 两条路径都必须走到，否则战斗剩余部分会一直卡在推近的机位上。
    //
    // 文档：docs/client/rendering_layout.md §四b、docs/mechanics/duel.md §5b
    // =========================================================================

    public sealed class StageCameraRig : MonoBehaviour
    {
        static StageCameraRig _instance;

        Camera _cam;
        bool _held;
        float _pitch, _distance;

        public static StageCameraRig Ensure()
        {
            if (_instance == null)
            {
                var cam = Camera.main;
                if (cam == null) return null;
                _instance = cam.GetComponent<StageCameraRig>()
                            ?? cam.gameObject.AddComponent<StageCameraRig>();
            }
            return _instance;
        }

        /// <summary>停播/重播时的兜底还位（编排层可能来不及走到 Release）。</summary>
        public static void ReleaseAll()
        {
            if (_instance != null) _instance.Release();
            else CameraShaker.Suspended = false;
        }

        /// <summary>在「原机位」与「目标机位」之间插值接管。p 会被钳到 0~1。
        /// p=0 时姿态与 CameraFitter 摆的完全一致，所以起手不会跳。</summary>
        public void Blend(float targetPitchDeg, float targetDistance, float p)
        {
            if (!EnsureCam()) return;
            p = Mathf.Clamp01(p);
            _pitch = Mathf.Lerp(CameraFitter.PilotPitchDeg, targetPitchDeg, p);
            _distance = Mathf.Lerp(CameraFitter.PilotDistance, targetDistance, p);
            if (!_held)
            {
                _held = true;
                CameraShaker.Suspended = true;
            }
            ApplyPose();
        }

        /// <summary>交还相机：立刻按 CameraFitter 的静态机位复位，并恢复抖动自写。</summary>
        public void Release()
        {
            CameraShaker.Suspended = false;
            if (!_held) return;
            _held = false;
            if (!EnsureCam()) return;
            _pitch = CameraFitter.PilotPitchDeg;
            _distance = CameraFitter.PilotDistance;
            ApplyBasePose();
        }

        bool EnsureCam()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            return _cam != null;
        }

        void LateUpdate()
        {
            // 每帧重写：CameraFitter 在分辨率变化时也会摆相机，接管期间必须压过它
            if (_held && EnsureCam()) ApplyPose();
        }

        void ApplyPose()
        {
            ApplyBasePose();
            // 抖动此刻是"只算不写"的偏移量，由本处唯一叠加
            _cam.transform.position += CameraShaker.CurrentOffset;
        }

        void ApplyBasePose()
        {
            // 与 CameraFitter.ApplyPerspectivePilot 同式：绕原点的俯角+距离极坐标
            float rad = _pitch * Mathf.Deg2Rad;
            _cam.transform.SetPositionAndRotation(
                new Vector3(0f, Mathf.Sin(rad) * _distance, -Mathf.Cos(rad) * _distance),
                Quaternion.Euler(_pitch, 0f, 0f));
        }
    }
}
