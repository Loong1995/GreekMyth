using System.Collections.Generic;
using System.Text;
using ClientBattle.VFX;
using UnityEditor;
using UnityEngine;

namespace GreekMyth.EditorTools
{
    /// <summary>标准件的用途。决定原料怎么选、挂什么运行期组件。</summary>
    public enum VfxUsage
    {
        /// <summary>定点件：在某个锚点原地播完（卡面/定位圆上空）。</summary>
        Anchor,
        /// <summary>地面定点件：同 Anchor，另挂 VfxGroundLayer（排序豁免+埋地救援）。</summary>
        Ground,
        /// <summary>罩身件（`shroud_` 前缀）：常驻包裹卡身。尺寸归挂载期
        /// `VfxShroudFitter`（不挂 VfxCircleFit）；**中和折射、不毁节点**——
        /// Distortion 只摘本节点渲染/粒子（保留子层如 ShieldAdd*，P-78），
        /// 并把游离 `LightningTrails*` 电弧摘掉（卡面尺度下读成全屏乱电）；
        /// 另摘 CollisionTrigger（舞台无碰撞体）。</summary>
        Shroud,
        /// <summary>场域氛围件（`ambient_` 前缀）：**不挂任何一张卡**，源点钉在
        /// 主战场地面中心，靠世界尺度铺满视野做整场氛围（雷暴/风沙/极光…）。
        /// 与罩身相反：这里要的就是那层大范围游离元素（`LightningTrails*` 等），
        /// 而包住人形的壳/折射层在场域尺度下既看不见又费；一律摘掉。
        /// 尺寸/高度/层序归 `StagePerformanceConfig.AmbientField*`（不挂 VfxCircleFit：
        /// 卡牌尺度的圆定径会把满场氛围缩成一小坨）。</summary>
        AmbientField,
    }

    // =========================================================================
    // 厂包特效标准化流水线（唯一入口，pack 无关）。
    //
    // 【为什么必须有统一流水线】2026-07-27 单挑三件连环事故复盘：
    //   ① 把投射物运载器当定点件用、删位移驱动 → 全部粒子层 rate=0，一颗粒子不出；
    //   ② Play 模式下接线 → RFX*_PerPlatformSettings 的 Awake 降配被烤进成品；
    //   ③ 删 Light/AudioSource 留下同节点曲线脚本 → Awake 抛 MissingComponentException
    //      从 Instantiate 传出，整段演出协程当场死掉。
    // 三个坑都不是"这一件的问题"，而是**裸写拷贝脚本必然会踩的结构性问题**。
    // 所以：任何厂包件晋升标准件，一律走本流水线；接线脚本只允许是清单。
    //
    // 【兼容性约定】不引用厂包类型（编辑器程序集也引用不到），一律按两条包际约定走：
    //   · 脚本类型名前缀 `RFX`（RFX1_* = Magic Pack v1，RFX4_* = Realistic v4，
    //     KriptoFX 系列全部满足）；
    //   · 投射物的碰撞爆发子件挂在位移脚本的序列化字段上：
    //     `EffectsOnCollision`（RFX1，数组）/ `EffectOnCollision`（RFX4，单个）。
    //   新包不满足约定时，在 ResolveAnchorSource / 驱动配对表里加分支，不另起脚本。
    //
    // 全程 PrefabUtility.LoadPrefabContents（纯资产编辑，不进场景、不跑 Awake），
    // 幂等可重跑，不改厂包原件。
    // =========================================================================

    public static class VfxPackStandardizer
    {
        public const string VfxDir = "Assets/Resources/ClientBattle/VFX";

        /// <summary>移动端每件保留的实时灯上限。厂包火/爆件的"热"很大一部分
        /// 来自地面受光，全删会发灰；留 1 盏关阴影是"接近画廊/能上手机"的折中。</summary>
        const int MaxLightsPerEffect = 1;

        // ---------------------------------------------- 移动端预算（红线，勿放宽）
        //
        // 依据见 docs/client/vfx_mobile_budget.md。三条硬红线都不是"调优"，
        // 是**中低端 Android 上会不会掉到 20 帧以下**的分界：
        //
        // ① 屏幕折射（Distortion/Refraction）：采样 `_CameraOpaqueTexture`，
        //    等于逼 URP 每帧多做一次全屏不透明拷贝。Tile 架构 GPU 上这是纯带宽
        //    支出（还会打断 tile 内渲染），而我们的低频大理石舞台上根本看不出
        //    折射（P-74）。**一律摘**，不分用途。摘干净后 Mobile RP Asset 才敢
        //    关 Opaque Texture——那才是真正的收益，单件省的只是它自己那点填充率。
        // ② 粒子碰撞/触发：World 模式按粒子做射线，Quality=High 更是逐粒子逐帧。
        //    我们的舞台**没有碰撞体**，算完也撞不到任何东西＝纯 CPU 浪费；
        //    `sendCollisionMessages` 还会往托管层回调。**一律关**。
        // ③ 活跃粒子总量：这条是**填充率**红线，也是本项目最被低估的一项。
        //    2026-07-28 实测：`cast_duel_launch` 单件峰值 ≈18700 活跃粒子
        //    （ParticlesRingLoop 一层 5000/s × 2.5s 寿命），单挑双方同放＝×2；
        //    `hit_massive` 两层各 burst 10000。半透明加色粒子的代价按**屏幕覆盖
        //    像素**算，中低端机（Adreno 6xx 以下 / Mali-G5x）几千颗铺满屏就是
        //    个位数帧。厂包按 PC 桌面做，这个量级必须由我们裁。
        //
        //    裁法是**按比例稀释**（burst 数与 rateOverTime 同乘一个系数），不是把
        //    maxParticles 一夹了事：硬夹只是发到上限就不发了，先到的粒子占满、
        //    后面的整段消失，形状会缺一块；等比稀释保持分布与节奏，只是变稀。

        /// <summary>单件**活跃粒子总量**预算（估算＝Σ(burst + rate×lifetime)）。
        /// 场域件铺满全场给得宽一点，但也正因为铺满全场，透支的就是填充率，
        /// 不能再宽。改这三个数＝改全项目观感与帧率的平衡点，改前先看
        /// `docs/client/vfx_mobile_budget.md` 的实测依据。</summary>
        static int ParticleBudgetOf(VfxUsage usage) => usage switch
        {
            VfxUsage.AmbientField => 2000,
            VfxUsage.Ground => 1500,
            _ => 1500,
        };

        /// <summary>驱动脚本 ↔ 被驱动组件的配对表（按类型名子串匹配，两包通用）。
        /// 删右边的组件时，同节点上名字含左边子串的 RFX 脚本必须一起删，
        /// 否则它们在 Awake 里取不到组件会抛异常（见 §复盘 ③）。</summary>
        static readonly (string Marker, System.Type Driven)[] DriverPairs =
        {
            ("Light", typeof(Light)),
            ("Audio", typeof(AudioSource)),
            ("Wind", typeof(WindZone)),
        };

        // ------------------------------------------------------------ 主流程

        /// <summary>把一件厂包特效标准化为 Resources 标准件。返回是否成功（含验证）。</summary>
        public static bool Standardize(string srcPath, string key, VfxUsage usage)
        {
            // Play 模式下 InstantiatePrefab 会进运行场景、脚本 Awake 的运行期突变
            // （如 PerPlatformSettings 的降配）会被 SaveAsPrefabAsset 烤进成品。
            // 本流水线虽然用 LoadPrefabContents 不受此害，但 Play 中改资产本身就是禁区。
            if (Application.isPlaying)
            {
                Debug.LogError($"[VfxPipe] 拒绝在 Play 模式下接线（{key}）：运行期状态会被烤进 prefab。");
                return false;
            }

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (src == null)
            {
                Debug.LogError($"[VfxPipe] 源缺失：{srcPath}");
                return false;
            }

            // 罩身/场域不做运载器改选：原料本身就是常驻件，不存在"飞到碰撞点"语义
            string redirect = null;
            string resolved = usage is VfxUsage.Shroud or VfxUsage.AmbientField
                ? srcPath : ResolveAnchorSource(srcPath, out redirect);
            string dest = $"{VfxDir}/{key}.prefab";
            System.IO.Directory.CreateDirectory(VfxDir);
            if (AssetDatabase.LoadAssetAtPath<Object>(dest) != null)
                AssetDatabase.DeleteAsset(dest);
            // CopyAsset 而非 Instantiate+Unpack：复制品天然是独立 Regular prefab，
            // 不经过场景实例，任何脚本都没机会执行。
            if (!AssetDatabase.CopyAsset(resolved, dest))
            {
                Debug.LogError($"[VfxPipe] 拷贝失败：{resolved} → {dest}");
                return false;
            }

            var log = new StringBuilder();
            if (redirect != null) log.AppendLine("  " + redirect);

            var root = PrefabUtility.LoadPrefabContents(dest);
            try
            {
                root.name = key;
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;

                int missing = CleanMissingScripts(root);
                if (missing > 0) log.AppendLine($"  清失效脚本槽 {missing}");
                StripScenePolluters(root, log);
                TrimAudio(root, log);
                TrimLights(root, log);
                StripDeadLayers(root, log);
                ApplyMobileBudget(root, usage, log);
                if (usage == VfxUsage.Shroud) StripShroudUnfit(root, log);
                if (usage == VfxUsage.AmbientField) StripAmbientUnfit(root, log);
                float shifted = NormalizeStartDelay(root);
                if (shifted > 0.001f) log.AppendLine($"  前移起播 -{shifted:F2}s");
                AttachUsageComponents(root, usage, log);

                PrefabUtility.SaveAsPrefabAsset(root, dest);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            bool ok = Verify(dest, out string report);
            Debug.Log($"[VfxPipe] {(ok ? "✓" : "✗")} {key} ← {resolved}\n{log}{report}");
            return ok;
        }

        // ------------------------------------------------ pass 1：原料改选

        /// <summary>定点用途的原料改选：**投射物运载器 → 碰撞爆发子件**。
        ///
        /// 厂包主件常是投射物系统：母件的粒子层按"移动距离"发射（静止＝零粒子），
        /// 位移脚本飞到碰撞点后**实例化另一个子 prefab**（那才是画廊里看到的爆炸），
        /// 同时把飞行期元素（灯/风/贴花/音频）全部关掉。所以：
        ///   · 把母件钉在原地＝一颗粒子都不出（不是"缩水"，是零）；
        ///   · 删掉位移脚本＝拔掉发射器，同样是零；
        ///   · **正确原料就是碰撞子件本身**——独立、一次性 burst、原地播全程。
        /// 判据与字段名是两包共同约定（见文件头「兼容性约定」）。</summary>
        static string ResolveAnchorSource(string srcPath, out string redirect)
        {
            redirect = null;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            foreach (var mb in src.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.Contains("TransformMotion") && !n.Contains("PhysicsMotion")) continue;

                var so = new SerializedObject(mb);
                var many = so.FindProperty("EffectsOnCollision"); // RFX1：数组
                if (many != null && many.isArray && many.arraySize > 0)
                {
                    var first = many.GetArrayElementAtIndex(0).objectReferenceValue;
                    if (first != null)
                    {
                        string path = AssetDatabase.GetAssetPath(first);
                        redirect = $"原料改选：{srcPath} 是投射物运载器，取其碰撞子件 {path}";
                        return path;
                    }
                }
                var one = so.FindProperty("EffectOnCollision"); // RFX4：单个
                if (one != null && one.objectReferenceValue != null)
                {
                    string path = AssetDatabase.GetAssetPath(one.objectReferenceValue);
                    redirect = $"原料改选：{srcPath} 是投射物运载器，取其碰撞子件 {path}";
                    return path;
                }
                Debug.LogWarning($"[VfxPipe] {srcPath} 带位移驱动但没找到碰撞子件字段，按原件继续（多半播不出来）。");
            }
            return srcPath;
        }

        // ------------------------------------------------ pass 2~6：清洗

        static int CleanMissingScripts(GameObject root)
        {
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                n += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
            return n;
        }

        /// <summary>摘掉会影响**本件之外**世界的组件：
        ///   · WindZone：场景级力场，会吹歪别的特效；
        ///   · RFX*_CameraShake：厂包直接晃 Camera.main，与自研 CameraShaker /
        ///     StageCameraRig 打架；
        ///   · RFX*_PerPlatformSettings：Awake 里按平台改发射率/预算——运行期
        ///     不确定性 + 编辑期误执行会烤进资产（§复盘 ②）。移动端预算由本流水线
        ///     显式裁剪，不留给厂包脚本自由发挥。</summary>
        static void StripScenePolluters(GameObject root, StringBuilder log)
        {
            foreach (var wind in root.GetComponentsInChildren<WindZone>(true))
                RemoveWithPairedDrivers(wind, log);

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.StartsWith("RFX")) continue;
                if (!n.Contains("CameraShake") && !n.Contains("PerPlatformSettings")) continue;
                log.AppendLine($"  摘 {n} @ {mb.gameObject.name}");
                Object.DestroyImmediate(mb, true);
            }
        }

        /// <summary>音源全删：厂包 playOnAwake 音效绕过我们的 SFX 总线且与自研音效撞车。</summary>
        static void TrimAudio(GameObject root, StringBuilder log)
        {
            int n = 0;
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true))
                n += RemoveWithPairedDrivers(audio, log);
            if (n > 0) log.AppendLine($"  去音源（连驱动）{n}");
        }

        /// <summary>实时灯限量：留 ≤MaxLightsPerEffect 盏、关阴影，其余连驱动删。</summary>
        static void TrimLights(GameObject root, StringBuilder log)
        {
            int kept = 0;
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (kept < MaxLightsPerEffect)
                {
                    light.shadows = LightShadows.None;
                    kept++;
                    continue;
                }
                RemoveWithPairedDrivers(light, log);
            }
            log.AppendLine($"  实时灯保留 {kept}");
        }

        /// <summary>删掉一个组件，**连同同节点上驱动它的 RFX 脚本**（按配对表匹配）。
        ///
        /// 【血的教训，§复盘 ③】只删组件、留下驱动脚本＝整段演出崩掉：
        /// `RFX*_LightCurves` 等在 Awake 里直取同节点组件，取不到就抛
        /// `MissingComponentException`，异常从 Instantiate 传出、PlayAt 抛错、
        /// 演出协程当场死——症状是"从这一刻起后面所有特效全没了"。
        /// 只删**名字与组件类型配对**的脚本（不是同节点全部 RFX 脚本）：
        /// 根节点常同时挂 EffectSettings 主驱动，一锅端会把整件的"大脑"删掉。</summary>
        static int RemoveWithPairedDrivers(Component target, StringBuilder log)
        {
            if (target == null) return 0;
            string marker = null;
            foreach (var (m, driven) in DriverPairs)
                if (driven.IsInstanceOfType(target)) { marker = m; break; }

            int removed = 0;
            if (marker != null)
            {
                foreach (var mb in target.GetComponents<MonoBehaviour>())
                {
                    if (mb == null) continue;
                    string n = mb.GetType().Name;
                    if (!n.StartsWith("RFX") || !n.Contains(marker)) continue;
                    Object.DestroyImmediate(mb, true);
                    removed++;
                }
            }
            log.AppendLine($"  摘 {target.GetType().Name} @ {target.gameObject.name}"
                           + (removed > 0 ? $"（连驱动 {removed}）" : string.Empty));
            Object.DestroyImmediate(target, true);
            return removed + 1;
        }

        /// <summary>死渲染层：Projector（URP 弃用）与厂包深度贴花（URP 画不出，P-33）。
        /// 摘掉的观感层若承担主要视觉，替代方案登记在调用侧（如地面焦痕→自研裂地）。</summary>
        static void StripDeadLayers(GameObject root, StringBuilder log)
        {
            foreach (var c in root.GetComponentsInChildren<Projector>(true))
            {
                log.AppendLine($"  摘 Projector @ {c.gameObject.name}");
                Object.DestroyImmediate(c, true);
            }
            var dead = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                if (shader.name != "KriptoFX/RFX1/Decal" && shader.name != "KriptoFX/RFX4/Decal") continue;
                if (r.gameObject == root) continue;
                dead.Add(r.gameObject);
            }
            foreach (var go in dead)
            {
                log.AppendLine($"  摘死贴花节点 {go.name}");
                Object.DestroyImmediate(go, true);
            }
        }

        /// <summary>移动端预算 pass（**全用途执行**，红线见本文件顶部常量区注释）：
        ///   ① 摘屏幕折射层；② 关粒子碰撞/触发；③ 夹 maxParticles 到用途预算。
        ///
        /// 【为什么放在这里而不是各用途分支】折射/碰撞/粒子上限与"这件怎么用"
        /// 无关，只与"跑在手机上"有关。放进用途分支的下场已经实测过：
        /// 折射只在 Shroud 分支摘，于是 Anchor/Ground 用途的 7 件带着 Distortion
        /// 一路上线，`_CameraOpaqueTexture` 至今关不掉（2026-07-28 全量排查，P-79）。</summary>
        static void ApplyMobileBudget(GameObject root, VfxUsage usage, StringBuilder log)
        {
            NeutralizeRefraction(root, log);

            int coll = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var collision = ps.collision;
                var trigger = ps.trigger;
                if (collision.enabled) { collision.enabled = false; coll++; }
                if (trigger.enabled) { trigger.enabled = false; coll++; }
            }
            if (coll > 0) log.AppendLine($"  关粒子碰撞/触发 ×{coll}（舞台无碰撞体，纯 CPU 浪费）");

            ThinToParticleBudget(root, ParticleBudgetOf(usage), log);
        }

        /// <summary>把整件的活跃粒子总量**等比稀释**到预算内，并把每层 maxParticles
        /// 收成该层稀释后的量（留 2 倍余量兜随机）。等比＝保形状保节奏，只变稀。</summary>
        static void ThinToParticleBudget(GameObject root, int budget, StringBuilder log)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            float total = 0f;
            foreach (var ps in systems) total += EstimateLive(ps);
            if (total <= budget)
            {
                foreach (var ps in systems)
                {
                    var main = ps.main;
                    int lay = Mathf.Max(16, Mathf.CeilToInt(EstimateLive(ps) * 2f));
                    if (main.maxParticles > lay) main.maxParticles = lay;
                }
                return;
            }

            float factor = budget / total;
            foreach (var ps in systems)
            {
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= factor;
                for (int i = 0; i < emission.burstCount; i++)
                {
                    var b = emission.GetBurst(i);
                    var c = b.count;
                    c.constantMin = Mathf.Max(1f, c.constantMin * factor);
                    c.constantMax = Mathf.Max(1f, c.constantMax * factor);
                    b.count = c;
                    emission.SetBurst(i, b);
                }
                var main = ps.main;
                main.maxParticles = Mathf.Max(16, Mathf.CeilToInt(EstimateLive(ps) * 2f));
            }
            log.AppendLine($"  粒子等比稀释 ×{factor:F3}（估算 {total:F0} → 预算 {budget}）");
        }

        /// <summary>单层活跃粒子估算：一次性 burst 总数 + 稳态 rate×lifetime。
        /// 不求精确（曲线/随机/子发射器都简化了），求的是**同一把尺子**——
        /// 用它排序谁是填充率大户、并作稀释系数的分母，够用。</summary>
        static float EstimateLive(ParticleSystem ps)
        {
            var e = ps.emission;
            if (!e.enabled) return 0f;
            float burst = 0f;
            for (int i = 0; i < e.burstCount; i++)
            {
                var b = e.GetBurst(i);
                burst += b.count.constantMax * Mathf.Max(1, b.cycleCount);
            }
            return burst + e.rateOverTimeMultiplier * ps.main.startLifetimeMultiplier;
        }

        /// <summary>摘屏幕折射层：**中和渲染、保节点与子层**。
        ///
        /// 折射壳常是别的层的父节点（Effect19 的 `Shield` 下挂 `ShieldAdd3`），
        /// 整节点删会连子层一起带走（P-78）。故只摘本节点的粒子系统与渲染器；
        /// 摘完若这个节点已经空到没子层、没别的组件，才顺手删掉空壳。</summary>
        static void NeutralizeRefraction(GameObject root, StringBuilder log)
        {
            var nodes = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                bool refractive =
                    shader.name.IndexOf("Distort", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || shader.name.IndexOf("Refract", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (refractive && !nodes.Contains(r.gameObject)) nodes.Add(r.gameObject);
            }
            foreach (var go in nodes)
            {
                if (go == null) continue;
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null) Object.DestroyImmediate(ps, true);
                foreach (var r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r, true);
                log.AppendLine($"  摘屏幕折射 {go.name}（_CameraOpaqueTexture 全屏拷贝，舞台上看不出）");
                bool empty = go.transform.childCount == 0
                             && go.GetComponents<Component>().Length <= 1 // 只剩 Transform
                             && go != root;
                if (empty) Object.DestroyImmediate(go, true);
            }
        }

        /// <summary>罩身专属清洗（P-77 / P-78）：
        ///   · **中和折射，不 Destroy 节点**：只摘本节点上 Distortion 的
        ///     ParticleSystem + Renderer。Effect19 等把 ShieldAdd* 挂在 Shield
        ///     （Distortion）子节点下——整 GO 删掉＝罩面加色层一起没，只剩
        ///     LightningTrails →「全屏乱电、看不见绕身罩」（P-78）。
        ///   · **摘游离 LightningTrails***：世界空间电弧，卡面尺度下糊满视野；
        ///     喷发射击粒子（Particles/Point 等）先提根再删电弧父节点。
        ///   · 摘 RFX*_CollisionTrigger。</summary>
        static void StripShroudUnfit(GameObject root, StringBuilder log)
        {
            // 折射层已由 ApplyMobileBudget.NeutralizeRefraction 统一中和（全用途红线），
            // 本方法只做罩身**专属**的两件事。

            // ---- 1) 游离电弧 LightningTrails*：提有用子层后删 ----
            var trails = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;
                if (t.name.IndexOf("LightningTrails", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                trails.Add(t);
            }
            // 先处理最深子节点，避免父删掉后子引用失效
            trails.Sort((a, b) => Depth(b).CompareTo(Depth(a)));
            foreach (var trail in trails)
            {
                if (trail == null) continue;
                // 非电弧子层（喷射 burst 等）提到根下，保留「罩身+喷射一下」的喷射
                for (int i = trail.childCount - 1; i >= 0; i--)
                {
                    var child = trail.GetChild(i);
                    if (child.name.IndexOf("LightningTrails",
                            System.StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    child.SetParent(root.transform, true);
                    log.AppendLine($"  提层 {child.name} ← {trail.name}");
                }
                log.AppendLine($"  摘游离电弧 {trail.name}（卡面尺度＝全屏乱电，P-78）");
                Object.DestroyImmediate(trail.gameObject, true);
            }

            // ---- 2) CollisionTrigger ----
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.StartsWith("RFX") || !n.Contains("CollisionTrigger")) continue;
                log.AppendLine($"  摘 {n} @ {mb.gameObject.name}");
                Object.DestroyImmediate(mb, true);
            }
        }

        /// <summary>场域氛围专属清洗：与罩身正好相反——**留游离元素、摘壳**。
        ///
        /// 厂包这类件里「包住人形的壳」（Distortion 折射壳及其加色子层、Fringe 环）
        /// 是按 2.5 米身位做的：钉在地面中心、放大到铺满战场后，壳既缩在中心一小坨
        /// 又是屏幕抓帧折射（低频舞台上本就不可见，P-74）。留下只有开销。
        /// 满场氛围靠的是 `LightningTrails` 这类世界空间游离层，正是罩身要摘的那层。
        /// 折射壳本身已由 ApplyMobileBudget 中和，这里补摘 Fringe 一类**壳专用**
        /// 加色环（不属于折射，但同样是照单人身位画的）。另摘 CollisionTrigger。</summary>
        static void StripAmbientUnfit(GameObject root, StringBuilder log)
        {
            var shells = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null || r.gameObject == root) continue;
                bool isShell =
                    shader.name.IndexOf("Distortion", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || shader.name.IndexOf("Fringe", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isShell) shells.Add(r.gameObject);
            }
            foreach (var go in shells)
            {
                if (go == null) continue;
                log.AppendLine($"  摘人形壳层 {go.name}（场域尺度下缩成一坨/不可见）");
                Object.DestroyImmediate(go, true);
            }

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.StartsWith("RFX") || !n.Contains("CollisionTrigger")) continue;
                log.AppendLine($"  摘 {n} @ {mb.gameObject.name}");
                Object.DestroyImmediate(mb, true);
            }
        }

        static int Depth(Transform t)
        {
            int d = 0;
            while (t != null) { d++; t = t.parent; }
            return d;
        }

        /// <summary>掐空转前摇：把所有层 startDelay **同时前移**，使最早会出图的层
        /// 从 0 起播（层间先后＝表演结构，故整体平移而非各自归零）。
        /// 判"会出图"只认 burst / rateOverTime——定点件永远静止，
        /// rateOverDistance 层不会出图，不能当基准。</summary>
        static float NormalizeStartDelay(GameObject root)
        {
            var systems = root.GetComponentsInChildren<ParticleSystem>(true);
            float lead = float.MaxValue;
            foreach (var ps in systems)
            {
                var emission = ps.emission;
                bool emits = emission.burstCount > 0 || emission.rateOverTimeMultiplier > 0f;
                if (!emits) continue;
                lead = Mathf.Min(lead, ps.main.startDelayMultiplier);
            }
            if (lead == float.MaxValue || lead <= 0.001f) return 0f;

            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startDelayMultiplier = Mathf.Max(0f, main.startDelayMultiplier - lead);
            }
            return lead;
        }

        // ------------------------------------------------ pass 7：运行期组件

        static void AttachUsageComponents(GameObject root, VfxUsage usage, StringBuilder log)
        {
            if (usage == VfxUsage.Ground && root.GetComponent<VfxGroundLayer>() == null)
                root.AddComponent<VfxGroundLayer>();

            // 定径：复刻画廊「C 键定径」。厂包件按 3D 世界尺度做（动辄 5~10 米），
            // 不定径接进来就是糊满全屏。基准取投影圆，与画廊一致。
            // 两个例外：罩身尺寸归挂载期 VfxShroudFitter（量壳定径 + 钉地环）；
            // 场域氛围尺寸归 StagePerformanceConfig.AmbientFieldScale（卡牌尺度的
            // 圆定径会把满场氛围缩成一小坨）。再挂 CircleFit 就是两个写方打架。
            if (usage is not (VfxUsage.Shroud or VfxUsage.AmbientField)
                && root.GetComponent<VfxCircleFit>() == null)
            {
                var fit = root.AddComponent<VfxCircleFit>();
                fit.Reference = VfxCircleFit.Circle.Projection;
                fit.Factor = 1f;
                fit.RescueIfBuried = usage == VfxUsage.Ground;
            }

            // 残留 RFX 驱动脚本的件不池化：复用不重跑 Awake/Start（P-66）
            bool driven = false;
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name.StartsWith("RFX")) { driven = true; break; }
            if (driven && root.GetComponent<VfxFreshInstance>() == null)
                root.AddComponent<VfxFreshInstance>();
            log.AppendLine((usage switch
                           {
                               VfxUsage.Shroud => "  [尺寸归 VfxShroudFitter]",
                               VfxUsage.AmbientField => "  [尺寸归 StagePerformanceConfig.AmbientField*]",
                               _ => "  [VfxCircleFit=投影圆]",
                           })
                           + (usage == VfxUsage.Ground ? " [VfxGroundLayer+埋地救援]" : string.Empty)
                           + (driven ? " [VfxFreshInstance 不池化]" : string.Empty));
        }

        // ------------------------------------------------ 落盘后验证

        /// <summary>标准件完成定义（静态可判的部分），一件不过整批报错：
        ///   1. missing script = 0（存盘环节可能丢组件，P-67）；
        ///   2. **自主可发射**：至少一层 burst&gt;0 或 rateOverTime&gt;0——
        ///      全否＝运载器选错原料，播出来是"完全没效果"；
        ///   3. **驱动配对完整**：每个带 Light/Audio/Wind 字样的 RFX 脚本，
        ///      同节点必须真的有对应组件——否则运行期 Awake 抛异常打断整段演出；
        ///   4. 能被 Instantiate（编辑器冒烟；注意厂包 Awake 只在 Play 跑，
        ///      此项挡不住 3 的问题，所以 3 必须静态查）。</summary>
        public static bool Verify(string path, out string report)
        {
            var sb = new StringBuilder();
            bool ok = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { report = $"  ✗ 加载不到 {path}\n"; return false; }

            int missing = 0;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missing > 0) { ok = false; sb.AppendLine($"  ✗ missing script ×{missing}（P-67）"); }

            // 可见性：要么有自主发射的粒子层（burst / rateOverTime），要么有
            // 非粒子渲染器（自研裂地贴花、弹道线是 Mesh/Line/Trail，不靠粒子出图）。
            // 两者皆无＝运载器层（rateOverDistance 只在移动时发射），定点播是零粒子。
            bool visible = false;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var e = ps.emission;
                if (e.burstCount > 0 || e.rateOverTimeMultiplier > 0f) { visible = true; break; }
            }
            if (!visible)
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer)) { visible = true; break; }
            if (!visible)
            {
                ok = false;
                sb.AppendLine("  ✗ 无任何可见层（粒子全是 rateOverDistance 运载器层，也无网格/线渲染器）"
                              + "——定点播出来是零粒子");
            }

            foreach (var mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (!n.StartsWith("RFX")) continue;
                foreach (var (marker, driven) in DriverPairs)
                {
                    if (!n.Contains(marker)) continue;
                    if (mb.GetComponent(driven) != null) continue;
                    ok = false;
                    sb.AppendLine($"  ✗ 孤儿驱动 {n} @ {mb.gameObject.name}：没有 {driven.Name}，"
                                  + "运行期 Awake 会抛异常并打断整段演出");
                }
            }

            // 移动端预算（红线见顶部常量区）：这三项不是"优化建议"，是中低端机
            // 掉不掉帧的分界，故与前四项同级，一票否决。
            int budget = ParticleBudgetOf(UsageOfKey(System.IO.Path.GetFileNameWithoutExtension(path)));
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var shader = r.sharedMaterial != null ? r.sharedMaterial.shader : null;
                if (shader == null) continue;
                if (shader.name.IndexOf("Distort", System.StringComparison.OrdinalIgnoreCase) < 0
                    && shader.name.IndexOf("Refract", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                ok = false;
                sb.AppendLine($"  ✗ 屏幕折射层 {r.name}（{shader.name}）：逼 URP 每帧全屏拷贝"
                              + "，且舞台上看不出——摘掉，见 vfx_mobile_budget.md");
            }
            float live = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                live += EstimateLive(ps);
                if (!ps.collision.enabled && !ps.trigger.enabled) continue;
                ok = false;
                sb.AppendLine($"  ✗ 粒子碰撞/触发未关 @ {ps.name}：舞台无碰撞体，纯 CPU 浪费");
            }
            if (live > budget)
            {
                ok = false;
                sb.AppendLine($"  ✗ 活跃粒子估算 {live:F0} 超预算 {budget}：半透明加色粒子按屏幕"
                              + "覆盖像素计价，中低端机会直接掉帧——等比稀释，别硬夹 maxParticles");
            }
            int audio = prefab.GetComponentsInChildren<AudioSource>(true).Length;
            if (audio > 0)
            {
                ok = false;
                sb.AppendLine($"  ✗ AudioSource ×{audio}：绕过 SFX 总线（多为流水线之前的存量件）");
            }
            int lights = prefab.GetComponentsInChildren<Light>(true).Length;
            if (lights > MaxLightsPerEffect)
            {
                ok = false;
                sb.AppendLine($"  ✗ 实时灯 ×{lights} 超上限 {MaxLightsPerEffect}");
            }

            GameObject probe = null;
            try { probe = Object.Instantiate(prefab); }
            catch (System.Exception e) { ok = false; sb.AppendLine($"  ✗ 实例化抛 {e.GetType().Name}：{e.Message}"); }
            finally { if (probe != null) Object.DestroyImmediate(probe); }

            if (ok) sb.AppendLine("  ✓ 验证通过（可发射 / 无孤儿驱动 / 无 missing / 可实例化 / 过移动端预算）");
            report = sb.ToString();
            return ok;
        }

        /// <summary>存量件没有记录用途，只能按 key 前缀反推（命名规范 §四.2 就是为此）。</summary>
        static VfxUsage UsageOfKey(string key) =>
            key.StartsWith("ground_", System.StringComparison.Ordinal) ? VfxUsage.Ground
            : key.StartsWith("shroud_", System.StringComparison.Ordinal) ? VfxUsage.Shroud
            : key.StartsWith("ambient_", System.StringComparison.Ordinal) ? VfxUsage.AmbientField
            : VfxUsage.Anchor;

        /// <summary>存量标准件**就地**清洗（幂等，可重跑）。
        ///
        /// 【为什么需要它，而不是"重跑接线"】Resources 里有一批件早于流水线：
        /// 部分是自研占位件、部分接线脚本已不在，**没有源可回溯**，重跑
        /// `Standardize` 无从谈起。但它们照样进包上机：2026-07-28 全量排查查出
        /// 20 件带 `playOnAwake` 音源、7 件带屏幕折射、5 件开着 World 粒子碰撞（P-79）。
        /// 所以清洗必须能对**成品**直接跑：用途按 key 前缀反推，只做与用途无关的
        /// 通用清洗（污染件/音源/灯/死层/移动端预算），**不碰**起播平移与用途组件
        /// （那两步不幂等，重跑会把节奏越挪越早）。</summary>
        [MenuItem("GreekMyth/特效/清洗 存量标准件（移动端预算：折射/碰撞/音源/灯/粒子上限）")]
        public static void CleanExistingAll()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[VfxPipe] 拒绝在 Play 模式下改资产。");
                return;
            }
            var log = new StringBuilder();
            int touched = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                total++;
                var one = new StringBuilder();
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int missing = CleanMissingScripts(root);
                    if (missing > 0) one.AppendLine($"  清失效脚本槽 {missing}");
                    StripScenePolluters(root, one);
                    TrimAudio(root, one);
                    TrimLights(root, one);
                    StripDeadLayers(root, one);
                    ApplyMobileBudget(root, UsageOfKey(key), one);
                    if (one.Length > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }

                // TrimLights 恒记一行"实时灯保留 N"，不能作为"动过"的判据
                if (one.ToString().Split('\n').Length > 2) { touched++; log.AppendLine(key + "\n" + one); }
            }
            AssetDatabase.SaveAssets();
            System.IO.File.WriteAllText("Temp/vfx_clean.txt", $"清洗 {total} 件，改动 {touched} 件\n{log}");
            Debug.Log($"[VfxPipe] 存量清洗 {total} 件，改动 {touched} 件（详情 Temp/vfx_clean.txt）");
        }

        /// <summary>全量体检：对 Resources 下所有标准件跑同一套验证。
        /// 老件（流水线之前接的）也在扫描范围——症状同源，早暴露早修。</summary>
        [MenuItem("GreekMyth/特效/体检 标准件流水线四项（发射/驱动配对/missing/实例化）")]
        public static void AuditAll()
        {
            var sb = new StringBuilder();
            int bad = 0, total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                total++;
                if (Verify(path, out string report)) continue;
                bad++;
                sb.AppendLine(System.IO.Path.GetFileNameWithoutExtension(path) + "\n" + report);
            }
            // 报告同时落盘：MCP/CI 读控制台只拿得到首行，多行详情必须进文件
            System.IO.File.WriteAllText("Temp/vfx_audit.txt",
                $"体检 {total} 件，{bad} 件不合格\n{sb}");
            if (bad > 0) Debug.LogWarning($"[VfxPipe] 体检 {total} 件，{bad} 件不合格（详情 Temp/vfx_audit.txt）：\n{sb}");
            else Debug.Log($"[VfxPipe] 体检 {total} 件全部通过。");
        }
    }
}
