using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    /// <summary>
    /// 【罩身类特效】的唯一定径/定位规格（2026-07-25 定稿）。
    ///
    /// 定义：把整张卡**罩在里面**的立体件（护盾罩、椭球、光柱、缠身火/雷）。
    /// 厂包这类件（RFX1 Effect31 等）都是照"一个站在原点的人形"做的：自带一个
    /// 包住身体的碰撞体，壳/贴花/电柱/碎石全按那个身位排布，并在被弹体撞到时
    /// 由 RFX1_ShieldCollisionTrigger 在命中点生成涟漪。
    ///
    /// 【三条规格】
    ///   1) **姿态世界竖直**（rotation = identity）。实测厂包这类件的壳本来就是
    ///      沿世界 Y 立着的竖柱：Effect31 的 Shield 在 identity 下包围盒
    ///      2.89 × **8.66(Y)** × 2.89、Lightning 2.45 × 7.36(Y) × 2.45、Decal 平躺。
    ///      所以**不要**给它补任何旋转 —— 跟卡同倾过，结果罩子斜着从卡面穿出去。
    ///   2) **定径基准 = 壳本体**（该件里 Y 向最高的那个渲染器），而不是整件包围盒、
    ///      也不是自带碰撞体。整件包围盒里混着世界空间模拟 + 重力飞散的碎石/烟，
    ///      随时间暴涨（Effect31 实测本地高度 0.12s ~10.5、后期 ~30），据此定径必错；
    ///      碰撞体是"被罩住的人形"（2.0×2.5）而不是罩子（2.89×8.66），按它定径会让
    ///      罩子高出卡顶三倍多 —— 在陡俯角下就读作"一根指着相机的柱子"。
    ///   3) **等比缩放**，系数取两条约束的**较大者**；**原点放投影圆心（贴地）**。
        ///      - 横向：结构件（壳 + 地面贴花）初算到投影圆直径 × WidthOvershoot；
    ///        **随后 Decal 单独钉死＝投影圆直径**（`PinGroundRingToProjectionCircle`），
    ///        竖向补高不得把地板圈撑出圆外；
    ///      - 竖向：**下限**是可见主体（壳以外的火/烟/电）顶到卡牌上边缘 × TopOvershoot；
    ///        与横向冲突时竖向优先，但横向溢出封顶 OverflowCap（只影响壳，贴花已钉死）。
    ///      高度**不另行压缩**：曾按"顶部＝卡上缘"把 y 单独缩到 0.29（壳 8.66 高、
    ///      卡只有 2.48 高），结果竖柱被压成一张薄饼，一眼就看出"这不是竖直的罩子"，
    ///      与同一件在棋盘中心等比展示时的观感完全不一致。厂包本身也是这个比例
    ///      （2.5 米的人形配 8.66 米高的壳），壳高过头顶就是它该有的样子。
    ///   4) **折射壳必须补轮廓**（EnsureShellVisible）。否则几何上盖过了卡，
    ///      屏幕上仍只剩脚下一坨火 —— 详见该方法注释。
    ///
    /// 【原点＝地面接触点，不要按壳底对齐】厂包这类件的**根原点就是脚下地面**
    /// （Effect31 的贴花在 y=0、碎石 y≈−0.06、电柱 y≈−0.2，只有壳中心抬到 2.34
    /// 且下半截 y≈−2 是故意埋进地面的）。曾把"壳的包围盒底面"对齐地面，
    /// 等于把整件抬高约 1.9 米 —— 贴花与碎石一起悬在半空，读作"地面痕迹跑出投影圆"。
    /// 壳下半截被不透明地面挡住，才是它该有的样子。
    ///
    /// 卡牌浮空（HoverRatio 卡高）不补偿：罩子从地面立起，自然把浮空段包进去。
    /// </summary>
    public static class VfxShroudFitter
    {
        /// <summary>缩放钳位：宁可略溢出，也不能缩成一个点。</summary>
        const float MinScale = 0.05f;
        const float MaxScale = 20f;

        /// <summary>量壳的仿真时刻（秒）。壳类粒子的 SizeOverLifetime 是关的
        /// （Effect31 实测），发射后尺寸不变，取 0.6s 是为了等它成形，
        /// 同时早于碎石飞散到最远。</summary>
        const float ShellSampleSeconds = 0.6f;

        /// <summary>补给折射壳的轮廓色（HDR，>1 才在 Bloom 下起圈）。
        /// 取冷青金，和奥林匹斯石台底图的暖灰不撞色。</summary>
        static readonly Color ShellRimColor = new Color(1.1f, 1.9f, 2.4f, 1f);
        /// <summary>轮廓收边指数：越大越只亮边缘（3 左右能看出球面而不糊成一坨）。</summary>
        const float ShellRimPow = 3f;
        /// <summary>轮廓处附加的扭曲量，给边缘一点"玻璃感"。</summary>
        const float ShellRimDistort = 400f;

        /// <summary>可见主体顶部要超出卡上缘的倍数。几何上"刚好齐平"在长焦俯视下
        /// 读起来仍像"没盖住"（卡上缘在屏幕上比罩顶低一截的错觉），
        /// 实测 15% 不够、35% 仍差一点，2026-07-26 定到 60%。</summary>
        const float TopOvershoot = 1.6f;

        /// <summary>横向切面要超出投影圆的倍数。罩身要"把卡包进去"，
        /// 切面与影子外接圆恰好同宽时，卡的左右边缘看着正好蹭着罩壁 —— 要露出
        /// 一圈罩壳才有包裹感。</summary>
        const float WidthOvershoot = 1.2f;

        /// <summary>为补高允许的最大横向溢出（×已含 WidthOvershoot 的横向系数）。
        /// 补高与"地面痕迹不出圈"天生冲突时，以这个上限收口。</summary>
        const float OverflowCap = 1.8f;

        /// <summary>把罩身件套到某张卡上。</summary>
        /// <param name="instance">已实例化的特效根节点。</param>
        /// <param name="cardAnchor">卡牌锚点（卡心世界坐标）。</param>
        public static void Fit(GameObject instance, Vector3 cardAnchor)
        {
            if (instance == null) return;

            instance.transform.rotation = Quaternion.identity;

            Measure(instance, out float structW, out float bodyTop, out Renderer shell);
            if (structW < 0.001f && bodyTop < 0.001f) { Restart(instance); return; }

            // 横向：投影圆再放大 WidthOvershoot，留出可见的包裹圈。
            float kFit = structW > 0.001f
                ? ArenaSlotLayout.ProjectionCircleDiameter * WidthOvershoot / structW : 0f;
            // 竖向下限：**可见主体**要盖过卡上缘（再多留 TopOvershoot 一截）。
            float coverH = (ArenaSlotLayout.CardTopY(cardAnchor) - CameraFitter.PilotGroundY)
                * TopOvershoot;
            float kCover = bodyTop > 0.001f ? coverH / bodyTop : 0f;

            // 竖向优先，但不许为了补高把地面痕迹撑出投影圆太多。
            float upper = kFit > 0.001f ? Mathf.Min(MaxScale, kFit * OverflowCap) : MaxScale;
            float k = Mathf.Clamp(Mathf.Max(kFit, kCover), MinScale, upper);

            instance.transform.localScale *= k;
            instance.transform.position = ArenaSlotLayout.ProjectionCircleCenter(cardAnchor);

            // 地板圈（Decal）严格＝投影圆：整件等比缩放常被竖向补高撑大，
            // 贴花会溢出圆外；单独把 Decal 反缩到直径＝ProjectionCircleDiameter。
            PinGroundRingToProjectionCircle(instance);

            EnsureShellVisible(shell);
            Restart(instance);
        }

        /// <summary>地面贴花/圈严格锚定投影圆：圆心已由根节点对齐；
        /// 仅把 Decal 类非粒子渲染器的水平尺寸钉成 <see cref="ArenaSlotLayout.ProjectionCircleDiameter"/>。
        /// 壳/火/烟仍保持整件等比，不受影响。可对外调用（跟随位移后重钉一次）。</summary>
        public static void PinGroundRingToProjectionCircle(GameObject instance)
        {
            float target = ArenaSlotLayout.ProjectionCircleDiameter;
            if (target < 0.001f || instance == null) return;

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                if (!IsDecal(r)) continue;

                float w = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                if (w < 0.001f) continue;
                float s = target / w;
                r.transform.localScale = new Vector3(
                    r.transform.localScale.x * s,
                    r.transform.localScale.y,
                    r.transform.localScale.z * s);

                var local = r.transform.localPosition;
                r.transform.localPosition = new Vector3(0f, local.y, 0f);
            }
        }

        /// <summary>粒子地面环焊死投影圆（CFXR Magic Aura A Runic 等）。
        /// 调用前根节点须已世界竖直、贴投影圆心；量指定环层水平包围盒，
        /// 整件等比缩到直径＝<see cref="ArenaSlotLayout.ProjectionCircleDiameter"/>。
        /// <para>
        /// 禁止用 <see cref="VfxCircleFit"/> 量整件：Rays/余波包络远大于符文环，
        /// 据此定径会把环压成投影圆内一小圈（P-88）。也禁止与 <see cref="VfxFitter"/>
        /// 同挂（两者都写 localScale）。
        /// </para></summary>
        /// <param name="ringLayerName">主环层名（CFXR＝`Runes`）；null＝跳过 Rays 后取最大粒子层。</param>
        public static void PinParticleRingToProjectionCircle(GameObject instance,
            string ringLayerName = "Runes")
        {
            float target = ArenaSlotLayout.ProjectionCircleDiameter;
            if (target < 0.001f || instance == null) return;

            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Simulate(0.12f, true, true);

            float extent = MeasureRingExtent(instance, ringLayerName);
            Restart(instance);
            if (extent < 0.001f) return;

            float k = Mathf.Clamp(target / extent, MinScale, MaxScale);
            instance.transform.localScale *= k;
        }

        static float MeasureRingExtent(GameObject instance, string ringLayerName)
        {
            float named = 0f;
            float exact = 0f;
            float fallback = 0f;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || r is not ParticleSystemRenderer) continue;
                string n = r.gameObject.name;
                if (n.IndexOf("Ray", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                float w = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                if (w < 0.001f) continue;
                fallback = Mathf.Max(fallback, w);
                if (string.IsNullOrEmpty(ringLayerName)) continue;
                if (string.Equals(n, ringLayerName, System.StringComparison.OrdinalIgnoreCase))
                    exact = Mathf.Max(exact, w);
                else if (n.IndexOf(ringLayerName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    named = Mathf.Max(named, w);
            }
            if (exact > 0.001f) return exact;
            if (named > 0.001f) return named;
            return fallback;
        }

        /// <summary>量「结构件」的水平最大边 —— 定径基准。
        ///
        /// 结构件 = **最高的那个可见壳粒子渲染器**。其余粒子（烟、碎石）是碎屑，
        /// 一律不算：它们世界空间模拟 + 重力飞散，包围盒随时间暴涨
        /// （Effect31 的 Smoke 4.44 宽、本地高度后期涨到 ~30），算进来会把主体挤瘪。
        ///
        /// 【两类层被排除在定径之外（2026-07-27 加，Effect18 实战）】
        ///   - **Decal 类网格**：它随后会被 <see cref="PinGroundRingToProjectionCircle"/>
        ///     单独钉死成投影圆直径，让一个「反正会被强制重设尺寸」的层决定整件缩放
        ///     上限是自相矛盾的。Effect18 的 `Decal2` 宽达 **8.97**（Effect31 才 3.4），
        ///     算进来 kFit 被压到约 0.3，竖向补高又受 OverflowCap 限制，
        ///     结果壳顶只到 1.7 米、卡上缘 3.3 米——就是「没罩住身」。
        ///   - **纯折射层**（shader 名含 Distortion 且**不是**唯一壳候选）：
        ///     它在我们的低频舞台上几乎不可见（详见 <see cref="EnsureShellVisible"/>），
        ///     却往往比可见壳大一大圈（Effect18 的 `Distortion` 7.42 宽 / 顶 4.48，
        ///     可见壳 `ShieldAdd` 只有 2.16 宽 / 顶 2.89）。拿它定径或当覆盖基准，
        ///     等于按「看不见的东西」判断「有没有罩住」。
        ///
        /// 同时输出 <paramref name="bodyTop"/> ＝ **除壳与折射层以外**的可见层
        /// （火/烟/电/发光壳面）顶面高出根原点多少 ＝ 竖向覆盖基准，
        /// 以及 <paramref name="shell"/> 本体（供补轮廓用）。
        ///
        /// 为什么竖向不拿壳算：壳多为折射壳，屏幕上贡献极弱。Effect31 实测壳顶已在
        /// 地面上方 6.5 米、卡上缘只有 3.3 米，按壳算"早就够了"，而人眼看到的火/烟
        /// 只到 3.0 米 —— 于是连续两轮被判"高度就是没到上边缘"。</summary>
        static void Measure(GameObject instance, out float width,
            out float bodyTop, out Renderer shell)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Simulate(ShellSampleSeconds, true, true);
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);

            // 壳＝可见粒子中最高者；全是折射层时才退回用折射层当壳（否则没壳可补轮廓）
            shell = TallestParticle(renderers, skipRefraction: true)
                    ?? TallestParticle(renderers, skipRefraction: false);

            float originY = instance.transform.position.y;
            width = 0f;
            bodyTop = 0f;
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (r is ParticleSystemRenderer)
                {
                    if (r != shell && !IsRefraction(r))
                        bodyTop = Mathf.Max(bodyTop, r.bounds.max.y - originY);
                    continue;
                }
                if (IsDecal(r)) continue; // 由 Pin 钉死，不参与定径
                width = Mathf.Max(width, r.bounds.size.x, r.bounds.size.z);
            }
            if (shell != null)
                width = Mathf.Max(width, shell.bounds.size.x, shell.bounds.size.z);
        }

        static Renderer TallestParticle(Renderer[] renderers, bool skipRefraction)
        {
            Renderer best = null;
            foreach (var r in renderers)
            {
                if (!r.enabled || r is not ParticleSystemRenderer) continue;
                if (skipRefraction && IsRefraction(r)) continue;
                if (best == null || r.bounds.size.y > best.bounds.size.y) best = r;
            }
            return best;
        }

        /// <summary>纯折射层（KriptoFX/RFX*/Distortion 等）：低频舞台上近乎不可见。</summary>
        static bool IsRefraction(Renderer r)
        {
            var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
            return shader != null &&
                   shader.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsDecal(Renderer r) =>
            r.gameObject.name.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            r.name.IndexOf("Decal", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>给折射壳补一层菲涅尔轮廓 —— 罩身件"看着高度不够"的真因。
        ///
        /// 厂包这类壳（Effect31 的 Shield，`KriptoFX/RFX1/Distortion`）出厂是
        /// **纯折射**：`_UseMainTex=0`、`_FresnelColor` 全黑、`_FresnelDistort=0`，
        /// 像素全靠采样 `_CameraOpaqueTexture` 做屏幕扭曲。厂包预览里背景是高频的
        /// 3D 场景，一扭就看得出来；我们舞台是低频大理石地面 + 天空板，
        /// 折射前后几乎同色 → 壳等于隐形。于是屏幕上只剩脚下那坨火，
        /// 读作"罩子高度完全不够"（连续两轮被指出都是这个原因，不是尺寸问题：
        /// 实测壳顶已在地面上方 6.5 米，卡上缘只有 3.3 米）。
        ///
        /// 只改**实例材质**（`Renderer.material` 而非 sharedMaterial），
        /// 不动包里的资产文件；只在检出"出厂全黑轮廓"时补，已配过色的件不覆盖。</summary>
        static void EnsureShellVisible(Renderer shell)
        {
            if (shell == null) return;
            var mat = shell.material;
            if (mat == null || !mat.HasProperty("_FresnelColor")) return;

            var rim = mat.GetColor("_FresnelColor");
            if (rim.maxColorComponent > 0.02f) return;

            mat.SetColor("_FresnelColor", ShellRimColor);
            if (mat.HasProperty("_FresnelPow")) mat.SetFloat("_FresnelPow", ShellRimPow);
            if (mat.HasProperty("_FresnelDistort"))
                mat.SetFloat("_FresnelDistort", ShellRimDistort);
        }

        static void Restart(GameObject instance)
        {
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }
    }
}
