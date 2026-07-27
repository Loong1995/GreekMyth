using ClientBattle.Units;
using UnityEngine;
using System.Collections.Generic;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】按**卡牌地面圆**定径 —— 把画廊里「C 键定径开」看到的那个
    // 尺寸原样搬到战场上。
    //
    // 【为什么 VfxFitter 不够】VfxFitter 做的是"布局变了跟着变"：
    // 缩放 = 当前卡宽 / 调参时卡宽 × Factor。它**不改变厂包件的原生尺寸**——
    // 一个 8 米宽的厂包件挂上 VfxFitter 之后还是 8 米宽，只是会随卡宽等比浮动。
    // 而画廊里看到的观感是**另外**按了一次定径（`FitToCardCircle`）缩到圆直径的，
    // 那一步此前只存在于画廊里。于是"画廊里挺好，接进去糊满全屏"就成了必然。
    // 本组件把画廊那一步搬到运行期，两边终于是同一个尺寸。
    //
    // 【量的是"起手核心"】Simulate 只推 0.12 s。推久了冲击件的碎屑已经飞散开，
    // 包围盒量到的是"整场余波"的尺寸，据此定径会把主体缩到看不见
    // （画廊实测曾缩到 ×0.13）。与 VfxGalleryRunner.MeasureCore 同一判据、同一时刻。
    //
    // 【一件只量一次】测量要跑 Simulate + 遍历渲染器，按 prefab 名缓存缩放比，
    // 池化复用与第二次播放直接取缓存，热路径零测量。
    //
    // 【与 VfxFitter 互斥】两者都写 localScale，同时挂必然打架。
    // 标准化工具（`VfxStandardizer.StandardizeAll`）见到本组件即跳过补挂 VfxFitter。
    //
    // 文档：docs/client/arena_stage.md §四c（两个圆）、
    //       docs/client/vfx_standardization.md、docs/client/vfx_pack_integration.md
    // =========================================================================

    public class VfxCircleFit : MonoBehaviour
    {
        public enum Circle
        {
            /// <summary>定位圆：心＝接地点，直径＝卡宽。脚下痕迹类。</summary>
            Anchor = 0,
            /// <summary>投影圆：整卡影子的外接圆，约 1.4 倍。**画廊定径用的就是它**，
            /// 要复刻画廊观感就选这个。</summary>
            Projection = 1,
        }

        public Circle Reference = Circle.Projection;

        /// <summary>额外倍率。调观感只调这一个数。</summary>
        public float Factor = 1f;

        /// <summary>整件都在地面以下时抬到刚露出地面（画廊 `RescueIfBuried` 的运行期版）。
        ///
        /// **只救"整件都埋了"的极端件**，不做"内容底面对齐地面"：厂包冲击件的原点
        /// 是**爆点**、内容绕原点上下对称，对齐底面等于把爆点抬到半空，读作
        /// "在空中炸"而不是"炸在脚下"。爆点落在圆心、下半截被不透明地面挡住，
        /// 才是命中该有的样子。抬的是特效，不是圆——圆心半径始终直取 ArenaSlotLayout。</summary>
        public bool RescueIfBuried;

        /// <summary>缩放钳位：宁可略溢出，也不能缩成一个点（与画廊同值）。</summary>
        const float MinRatio = 0.25f;
        const float MaxRatio = 20f;
        const float MeasureSeconds = 0.12f;

        /// <summary>prefab 名 → (量测时的圆直径, 缩放比, 埋地抬升)。带上直径是必须的：
        /// 换机型/改卡尺会让圆变大变小，直径一变就得重量，否则会沿用上一场的尺寸。
        /// 预热期（布局可能还没算完）量到的值也靠这一条自动作废。</summary>
        static readonly Dictionary<string, (float Diameter, float Ratio, float Lift)> _ratioCache = new();

        Vector3 _baseScale;
        bool _captured;
        float _pendingLift;

        void Awake() => Capture();

        void Capture()
        {
            if (_captured) return;
            _baseScale = transform.localScale;
            _captured = true;
        }

        void OnEnable()
        {
            Capture(); // 池化复用时 Awake 只走过一次
            _pendingLift = 0f;
            float target = TargetDiameter();
            if (target <= 0.0001f) return;

            var (ratio, lift) = Fit(target);
            float k = ratio * Factor;
            transform.localScale = _baseScale * k;
            // lift 是量测时（未缩放）的几何量，绕自身原点缩放后等比放大
            if (RescueIfBuried) _pendingLift = lift * k;
        }

        /// <summary>抬升必须等到 <b>LateUpdate</b> 才施加：`PlayAt` 的顺序是
        /// 先激活（OnEnable 在这里跑）、**后**写 position，在 OnEnable 里改位置
        /// 会被随后那句赋值原样抹掉。这类"组件想改的量被调用方后写覆盖"的时序坑
        /// 不会报错，只会静默失效。</summary>
        void LateUpdate()
        {
            if (_pendingLift <= 0.0001f) return;
            transform.position += Vector3.up * _pendingLift;
            _pendingLift = 0f;
        }

        float TargetDiameter() => Reference == Circle.Anchor
            ? ArenaSlotLayout.AnchorCircleDiameter
            : ArenaSlotLayout.ProjectionCircleDiameter;

        /// <summary>(基准缩放, 埋地抬升)。按 prefab 名缓存——同一件的原生几何不会变。
        /// 两个量共用**同一次** Simulate 量测，抬升不额外付代价。</summary>
        (float Ratio, float Lift) Fit(float targetDiameter)
        {
            // 池化实例名带 "(Clone)"，去掉后同一 prefab 的多个实例共用一条缓存
            string key = gameObject.name.Replace("(Clone)", string.Empty).Trim();
            if (_ratioCache.TryGetValue(key, out var cached)
                && Mathf.Approximately(cached.Diameter, targetDiameter))
                return (cached.Ratio, cached.Lift);

            var fit = Measure(targetDiameter);
            _ratioCache[key] = (targetDiameter, fit.Ratio, fit.Lift);
            return fit;
        }

        (float Ratio, float Lift) Measure(float targetDiameter)
        {
            var systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.Simulate(MeasureSeconds, true, true);

            bool any = false;
            Bounds bounds = default;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            // 量完必须重启，否则这一件是从 0.12 s 的中途开始播的（少了起手那一下）。
            // 与 VFXManager.RestartParticles 同规则：只在最上层起播，避免子发射器重触发。
            foreach (var ps in systems) ps.Clear(true);
            foreach (var ps in systems)
                if (TopMost(ps)) ps.Play(true);

            if (!any) return (1f, 0f);

            float extent = Mathf.Max(bounds.size.x, bounds.size.z);
            float ratio = extent > 0.001f
                ? Mathf.Clamp(targetDiameter / extent, MinRatio, MaxRatio)
                : 1f;

            // 相对**自身原点**算，与当前所在位置无关，故可随 prefab 缓存
            float originY = transform.position.y;
            float lift = bounds.max.y < originY
                ? originY - bounds.max.y + bounds.size.y * 0.5f
                : 0f;
            return (ratio, lift);
        }

        bool TopMost(ParticleSystem ps)
        {
            for (var t = ps.transform.parent; t != null && t != transform.parent; t = t.parent)
                if (t.GetComponent<ParticleSystem>() != null) return false;
            return true;
        }
    }
}
