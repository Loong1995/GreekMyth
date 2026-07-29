using System.Collections;
using System.Collections.Generic;
using ClientBattle.Placeholder;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层 基础设施】VFXManager：对象池 + PlayAt/PlayOn + 自动回收。
    //
    // 资源回退（key → 实例）：
    //   1. Resources/ClientBattle/VFX/<key>（Prefab，你后续上传真实特效）
    //   2. PlaceholderFactory 纯色 Sprite 方块（缩放弹跳示意）
    // 池：Dictionary<key, Queue<GameObject>>；实例播完自动回收进池。
    // =========================================================================

    public class VFXManager : MonoBehaviour
    {
        public static VFXManager Instance { get; private set; }

        readonly Dictionary<string, Queue<GameObject>> _pool = new();
        readonly Dictionary<string, GameObject> _prefabCache = new();
        readonly List<(string key, GameObject go)> _staged = new(); // 预热中待回收实例
        GameObject _prewarmRig;                                     // 预热离屏相机（含 RT）

        public static VFXManager Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("VFXManager");
                Instance = go.AddComponent<VFXManager>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>在世界坐标播一个特效，duration 秒后自动回收。</summary>
        public GameObject PlayAt(string key, Vector3 position, float duration = 0.6f, Color? tint = null)
        {
            var instance = Rent(key, tint);
            instance.transform.SetParent(transform, false);
            instance.transform.position = position;
            StartCoroutine(RecycleAfter(key, instance, duration));
            return instance;
        }

        /// <summary>挂在目标 Transform 上播（跟随移动），duration&lt;=0 表示常驻（调用方负责 Release）。</summary>
        public GameObject PlayOn(string key, Transform target, float duration = 0.6f,
                                 Vector3 offset = default, Color? tint = null)
        {
            var instance = Rent(key, tint);
            instance.transform.SetParent(target, false);
            instance.transform.localPosition = offset;
            if (duration > 0f)
                StartCoroutine(RecycleAfter(key, instance, duration));
            return instance;
        }

        /// <summary>该 key 的**一次性发射窗口**时长（秒），即"这一炸放完了"的时刻。
        ///
        /// 取各粒子系统 `startDelay + duration` 的最大值，有两条刻意的排除：
        ///
        /// · **不加 `startLifetime`**：厂包件普遍是「爆发 + 长烟尾」，把烟尾也等完，
        ///   观众看到的是一段发呆。发射结束＝主体已打完，余烬继续飘不妨碍下一拍。
        ///
        /// · **跳过 `loop=true` 的层**：循环层没有"播完"这回事（它靠外部停止），
        ///   它的 `duration` 是**循环周期**而不是时长，拿来当结束时刻是把两个
        ///   不同量当成一个用。混进来会得到一个既不是周期也不是时长的数
        ///   （实测 `cast_duel_launch` 因此报 4.0 s，而它真正的一次性爆发只有 1.5 s）。
        ///   循环层由调用方在合适的拍子上 `StopEmitting`。
        ///
        /// **全是循环层时**（`aura_duel_victory` / `ground_duel_defeat` 即如此）
        /// 退而求其次，返回「成形时长」＝各层 `startDelay + startLifetime` 的最大值：
        /// 循环件没有终点，但它从起播到**看上去完整**大约就是一个粒子寿命
        /// （粒子要先填满那个形状）。用它当节拍，比退到通用保底值贴合得多——
        /// 保底值只是"素材缺失时别把节奏丢了"，不该拿来当正常时长用。
        /// 只探测 prefab（不实例化），结果随 prefab 缓存；key 不存在返回 0。
        /// <paramref name="cap"/> 是硬上限——厂包件时长不可控，演出不能被卡住。
        ///
        /// 【注意是真实秒】粒子按真实时间播，不吃 <c>ctx.Scaled</c>。要"等它播完"
        /// 就只能等真实时长，把这段乘倍速等于把它拦腰截断（那就不叫顺序播了）。</summary>
        public float EmitWindow(string key, float cap)
        {
            if (string.IsNullOrEmpty(key) || cap <= 0f) return 0f;
            if (!_prefabCache.TryGetValue(key, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
                _prefabCache[key] = prefab;
            }
            if (prefab == null) return 0f;

            float window = 0f, shape = 0f;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                float delay = main.startDelayMultiplier;
                if (main.loop)
                    shape = Mathf.Max(shape, delay + main.startLifetimeMultiplier);
                else
                    window = Mathf.Max(window, delay + main.duration);
            }
            return Mathf.Min(window > 0f ? window : shape, cap);
        }

        /// <summary>让实例**收势**：只掐新粒子，已生成的按自己的 lifetime 走完。
        ///
        /// 顺序演出交接用。与"等它彻底播完"是两回事：循环层永远不会自己结束，
        /// 而全速发射中的件被下一拍盖过去会读作"炸到一半被打断"。在切拍那一刻
        /// 收势，余烬在下一拍里继续飘，读作"在余烬中被拽走"。</summary>
        public static void StopEmitting(GameObject instance)
        {
            if (instance == null) return;
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>渲染级预热是否已收尾（PlayLoop 等它变 true 再开播，约 3 帧）。</summary>
        public bool PrewarmComplete { get; private set; } = true;

        /// <summary>开战前渲染级预热：全部特效 prefab 各实例化 1 份，摆到远离棋盘的
        /// 离屏预热相机前激活并强制发射粒子，实际渲染 3 帧——把 shader 编译、
        /// 贴图上传、粒子网格建立全部压进加载期，然后回收入池。
        /// 仅入池不渲染是不够的：首次真正画到屏幕那帧仍会付编译/上传代价。</summary>
        public void Prewarm()
        {
            FinishPrewarm(); // 上一次预热被重播/跳过打断时先收尾
            PrewarmComplete = false;

            var prefabs = Resources.LoadAll<GameObject>("ClientBattle/VFX");
            if (prefabs.Length == 0) { PrewarmComplete = true; return; }

            // 离屏预热区：远离棋盘（y=3000），小 RT 相机只看这里，不上屏
            _prewarmRig = new GameObject("VfxPrewarmRig");
            _prewarmRig.transform.position = new Vector3(0f, 3000f, 0f);
            var camGo = new GameObject("PrewarmCamera");
            camGo.transform.SetParent(_prewarmRig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -30f);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 45f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.targetTexture = new RenderTexture(128, 128, 16);

            int i = 0;
            foreach (var prefab in prefabs)
            {
                // 备份/过渡件不应在 Resources；若漏网则跳过预热，避免占池、顶偏画廊序号。
                if (prefab == null || !VfxResourcesFilter.IsOursGalleryItem(prefab.name))
                    continue;
                _prefabCache[prefab.name] = prefab;
                // 同会话内重播：池里已有实例说明上一场已渲染过，shader/贴图已热
                if (_pool.TryGetValue(prefab.name, out var pooled) && pooled.Count > 0) continue;
                GameObject instance;
                // 一件坏件不允许拖垮整个预热批次（预热要实例化全部标准件）
                try { instance = Instantiate(prefab); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VFX] 预热 {prefab.name} 实例化抛 {e.GetType().Name}，跳过：{e.Message}");
                    continue;
                }
                instance.AddComponent<VfxOriginalScale>().Value = instance.transform.localScale;
                instance.transform.SetParent(_prewarmRig.transform, false);
                instance.transform.localPosition = new Vector3(
                    i % 6 * 12f - 30f, i / 6 * 12f - 24f, 0f);
                foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Play();
                    ps.Emit(2); // 无活粒子则渲染器不出图，warm 不到 shader/贴图
                }
                _staged.Add((prefab.name, instance));
                i++;
            }
            StartCoroutine(FinishPrewarmAfterFrames(3));
        }

        IEnumerator FinishPrewarmAfterFrames(int frames)
        {
            for (int f = 0; f < frames; f++) yield return null;
            FinishPrewarm();
        }

        void FinishPrewarm()
        {
            foreach (var (key, staged) in _staged)
            {
                if (staged == null) continue;
                foreach (var ps in staged.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Clear();
                Release(key, staged);
            }
            _staged.Clear();
            if (_prewarmRig != null)
            {
                var cam = _prewarmRig.GetComponentInChildren<Camera>();
                if (cam != null && cam.targetTexture != null)
                {
                    var rt = cam.targetTexture;
                    cam.targetTexture = null;
                    rt.Release();
                    Destroy(rt);
                }
                Destroy(_prewarmRig);
                _prewarmRig = null;
            }
            PrewarmComplete = true;
        }

        /// <summary>手动回收常驻特效（如整局光环随状态移除撤下）。
        /// 挂了 <see cref="VfxFreshInstance"/> 的件**销毁而不入池**（原因见该类注释）。</summary>
        public void Release(string key, GameObject instance)
        {
            if (instance == null) return;
            if (instance.GetComponent<VfxFreshInstance>() != null)
            {
                Destroy(instance);
                return;
            }
            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
            if (!_pool.TryGetValue(key, out var queue))
                _pool[key] = queue = new Queue<GameObject>();
            queue.Enqueue(instance);
        }

        /// <summary>清空场上全部飞行中特效（跳过/快进/重播时调用）。</summary>
        public void CancelAll()
        {
            StopAllCoroutines();
            // 仅 SetActive(false) 停不了 DOTween；弹道 Sequence 会继续改 transform → 重播叠影
            foreach (Transform child in transform)
            {
                if (child == null) continue;
                DOTween.Kill(child, complete: false);
                DOTween.Kill(child.gameObject, complete: false);
                foreach (var ps in child.GetComponentsInChildren<ParticleSystem>(true))
                    ps.Clear(true);
                // 一次性件不入池，只 SetActive(false) 会让它作为失活子物体永久挂在
                // 管理器下（既占内存又会被后续 foreach 反复遍历）
                if (child.GetComponent<VfxFreshInstance>() != null) Destroy(child.gameObject);
                else child.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------- 内部

        GameObject Rent(string key, Color? tint)
        {
            // 一次性件绕过池：它们的观感由 Awake/Start 里初始化的驱动脚本决定，
            // 而池化复用**不会重跑 Awake/Start**（Unity 语义），出池的是上一次
            // 结束时的残留状态。详见 VfxFreshInstance。
            if (IsFresh(key))
            {
                var fresh = Build(key, tint);
                fresh.AddComponent<VfxOriginalScale>().Value = fresh.transform.localScale;
                RestartParticles(fresh);
                EnsureVfxSorting(fresh);
                return fresh;
            }

            if (_pool.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                // 复位缩放：调用方可能改过 localScale（如追击斩击放大），回池复用要还原
                var stamp = pooled.GetComponent<VfxOriginalScale>();
                if (stamp != null) pooled.transform.localScale = stamp.Value;
                pooled.transform.localRotation = Quaternion.identity;
                pooled.SetActive(true);
                RestartParticles(pooled); // DualBolt 等 playOnAwake=0，不手动 Play 则无粒子
                EnsureVfxSorting(pooled);
                return pooled;
            }
            var built = Build(key, tint);
            built.AddComponent<VfxOriginalScale>().Value = built.transform.localScale;
            RestartParticles(built);
            EnsureVfxSorting(built);
            return built;
        }

        /// <summary>该 key 是否是「每次新建」的一次性件。查 prefab 上的标记，
        /// 结果随 prefab 缓存，热路径不做 IO。</summary>
        bool IsFresh(string key)
        {
            if (!_prefabCache.TryGetValue(key, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
                _prefabCache[key] = prefab;
            }
            return prefab != null && prefab.GetComponent<VfxFreshInstance>() != null;
        }

        /// <summary>出池/新建后强制重播粒子（Assets/VFX DualBolt 等 playOnAwake=false）。
        ///
        /// **只对"最上层"的粒子系统调 Play**：`Play(true)` 本身就会级联到全部子孙，
        /// 对每一层都再调一次等于把子发射器重复触发，相位被打乱——同一件在画廊里
        /// （只在根级起播）与战斗里长得不一样，这是其中一处。
        /// `Clear` 则对每一层都调：清空是幂等的，且深层残留粒子必须清干净。</summary>
        static void RestartParticles(GameObject instance)
        {
            if (instance == null) return;
            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems) ps.Clear(true);
            foreach (var ps in systems)
                if (IsTopMost(ps, instance.transform)) ps.Play(true);
        }

        /// <summary>ps 之上（到 root 为止）是否再无粒子系统。</summary>
        static bool IsTopMost(ParticleSystem ps, Transform root)
        {
            for (var t = ps.transform.parent; t != null && t != root.parent; t = t.parent)
                if (t.GetComponent<ParticleSystem>() != null) return false;
            return true;
        }

        /// <summary>Vefects 等源 Prefab sortingOrder=0，会被卡面盖住；抬到池默认档。
        /// 地面层特效（VfxGroundLayer）豁免：它们必须留在卡牌之下。
        ///
        /// 遍历 Renderer 基类而非只遍历 ParticleSystemRenderer：厂包大招里的护盾/
        /// 冲击波/尖刺/锁链/岩石都是 MeshRenderer 或 Trail/LineRenderer，源 prefab
        /// sortingOrder=0，只抬粒子会让这些层被卡牌立绘盖住（2026-07-25 定案）。</summary>
        static void EnsureVfxSorting(GameObject instance)
        {
            if (instance == null) return;
            if (instance.GetComponent<VfxGroundLayer>() != null) return;
            const int minOrder = 45;
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r is SpriteMask) continue; // 遮罩不参与排序抬升
                if (r.sortingOrder < minOrder) r.sortingOrder = minOrder;
            }
        }

        GameObject Build(string key, Color? tint)
        {
            if (!_prefabCache.TryGetValue(key, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
                _prefabCache[key] = prefab; // 缓存 null 也记住，避免反复 IO
            }
            if (prefab != null)
            {
                // 厂包脚本 Awake 里的异常会从 Instantiate 传出来。这里必须接住：
                // 一件坏件只允许它自己降级成占位，**不允许打断调用方的演出协程**
                // ——否则症状是"从这一刻起后面所有演出全没了"，且极难归因（P-68）。
                // 客户端播放红线「任何情况必能播出」的兜底就在这一层。
                try
                {
                    return Instantiate(prefab);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VFX] {key} 实例化抛 {e.GetType().Name}，降级为占位方块。"
                                   + $"多半是标准化裁剪产生的孤儿驱动脚本，"
                                   + $"跑一次「体检 标准件流水线四项」定位：{e.Message}");
                }
            }

            // 占位：纯色方块 + 出生缩放弹跳
            var go = new GameObject($"vfx_{key}");
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderFactory.GetSprite("VFX", key, tint ?? ColorOf(key), 48);
            renderer.sortingOrder = 40;
            go.AddComponent<PlaceholderPulse>();
            return go;
        }

        /// <summary>占位配色按 key 哈希取色相，保证不同特效肉眼可区分。</summary>
        static Color ColorOf(string key)
        {
            int hash = 0;
            foreach (char c in key) hash = hash * 31 + c;
            return Color.HSVToRGB(Mathf.Abs(hash % 360) / 360f, 0.75f, 1f);
        }

        /// <summary>回收宽限上限（秒）：停止发射后最多再等这么久让余烬自然消亡。
        /// 有上限是必须的——厂包件常有 5 s 以上的长尾粒子，无限等会让实例迟迟不回池。</summary>
        const float RecycleGrace = 1.2f;

        /// <summary>到点后**先停发射、再等余烬散尽**，而不是直接 SetActive(false)。
        ///
        /// 直接关等于拦腰砍断：屏幕上正飘着的火星/烟一帧消失，观感是"特效坏了"，
        /// 而画廊里同一件是自然收尾的——这是两处观感差异里最容易被误读成
        /// "素材不行"的一处。`StopEmitting` 只掐新粒子，已生成的按自己的
        /// lifetime 走完，形状与画廊一致。</summary>
        IEnumerator RecycleAfter(string key, GameObject instance, float duration)
        {
            yield return new WaitForSeconds(duration);
            if (instance == null) yield break;

            var systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            for (float t = 0f; t < RecycleGrace; t += Time.deltaTime)
            {
                if (instance == null) yield break;
                bool alive = false;
                foreach (var ps in systems)
                {
                    if (ps == null || !ps.IsAlive(true)) continue;
                    alive = true;
                    break;
                }
                if (!alive) break;
                yield return null;
            }
            Release(key, instance);
        }
    }


    /// <summary>记录实例出生缩放，回池复用时还原（防调用方缩放残留）。</summary>
    public class VfxOriginalScale : MonoBehaviour
    {
        public Vector3 Value = Vector3.one;
    }

    /// <summary>占位特效的呼吸/弹跳动画（真实 Prefab 到位后自然不再走这条路）。</summary>
    public class PlaceholderPulse : MonoBehaviour
    {
        float _age;

        void OnEnable() { _age = 0f; transform.localScale = Vector3.one * 0.2f; }

        void Update()
        {
            _age += Time.deltaTime;
            float s = 0.2f + Mathf.Sin(Mathf.Min(_age * 6f, Mathf.PI)) * 0.8f;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
