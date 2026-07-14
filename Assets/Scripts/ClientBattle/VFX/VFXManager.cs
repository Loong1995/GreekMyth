using System.Collections;
using System.Collections.Generic;
using ClientBattle.Placeholder;
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
                _prefabCache[prefab.name] = prefab;
                // 同会话内重播：池里已有实例说明上一场已渲染过，shader/贴图已热
                if (_pool.TryGetValue(prefab.name, out var pooled) && pooled.Count > 0) continue;
                var instance = Instantiate(prefab);
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

        /// <summary>手动回收常驻特效（如整局光环随状态移除撤下）。</summary>
        public void Release(string key, GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
            if (!_pool.TryGetValue(key, out var queue))
                _pool[key] = queue = new Queue<GameObject>();
            queue.Enqueue(instance);
        }

        /// <summary>清空场上全部飞行中特效（跳过/快进时调用）。</summary>
        public void CancelAll()
        {
            StopAllCoroutines();
            foreach (Transform child in transform)
                child.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------- 内部

        GameObject Rent(string key, Color? tint)
        {
            if (_pool.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                // 复位缩放：调用方可能改过 localScale（如追击斩击放大），回池复用要还原
                var stamp = pooled.GetComponent<VfxOriginalScale>();
                if (stamp != null) pooled.transform.localScale = stamp.Value;
                pooled.SetActive(true);
                return pooled;
            }
            var built = Build(key, tint);
            built.AddComponent<VfxOriginalScale>().Value = built.transform.localScale;
            return built;
        }

        GameObject Build(string key, Color? tint)
        {
            if (!_prefabCache.TryGetValue(key, out var prefab))
            {
                prefab = Resources.Load<GameObject>($"ClientBattle/VFX/{key}");
                _prefabCache[key] = prefab; // 缓存 null 也记住，避免反复 IO
            }
            if (prefab != null)
                return Instantiate(prefab);

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

        IEnumerator RecycleAfter(string key, GameObject instance, float duration)
        {
            yield return new WaitForSeconds(duration);
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
