using System.Collections.Generic;
using ClientBattle.Names;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 飘字服务（client_perform 硬性要求）：
    // 所有伤害/治疗/状态都在英雄头上飘字，包括结算数量和对应技能名。
    // 池化 TextMesh；同一单位连续飘字自动向上错位避免重叠。
    // =========================================================================

    public class FloatingTextService : MonoBehaviour
    {
        public static FloatingTextService Instance { get; private set; }

        readonly Queue<TextMesh> _pool = new();
        readonly Queue<ActiveFloat> _recordPool = new();
        readonly Dictionary<int, int> _stackDepth = new(); // unit instanceId → 在飘条数
        readonly List<ActiveFloat> _active = new();

        // 统一驱动的轻量记录：不为每条飘字创建 DOTween Sequence/Tween/闭包。
        sealed class ActiveFloat
        {
            public TextMesh Mesh;
            public int UnitKey;
            public Vector3 Start;
            public Color Color;
            public float Age;
        }

        FloatingTextTuning _tuning;
        FloatingTextTuning Tuning => _tuning ??= FloatingTextTuning.LoadOrDefault();

        public static FloatingTextService Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("FloatingTextService");
                Instance = go.AddComponent<FloatingTextService>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // TextMesh 按各自 fontSize 缓存字形：飘字 48 / 气泡台词 44 / 名字 42 / 兵力 30
        static readonly int[] GlyphSizes = { 30, 42, 44, 48 };

        /// <summary>开战前预热飘字对象，避免第一次密集结算时连续创建
        /// GameObject/TextMesh/MeshRenderer 造成主线程尖峰。
        /// extraCharacters 传本场战报将出现的台词/名字等动态文本：动态字体首次
        /// 遇到新字符会同步扩纹理并重建全部文本网格（明显掉帧），全部前置。</summary>
        public void Prewarm(int count = 24, string extraCharacters = null)
        {
            int missing = Mathf.Max(0, count - _pool.Count);
            Font runtimeFont = null;
            for (int i = 0; i < missing; i++)
            {
                var mesh = CreateMesh();
                runtimeFont ??= mesh.font;
                mesh.gameObject.SetActive(false);
                _pool.Enqueue(mesh);
                _recordPool.Enqueue(new ActiveFloat());
            }
            if (runtimeFont == null && _pool.Count > 0) runtimeFont = _pool.Peek().font;
            if (runtimeFont == null) return;
            string chars = ChineseNames.FloatingTextCharacters() + (extraCharacters ?? "");
            foreach (int size in GlyphSizes)
                runtimeFont.RequestCharactersInTexture(chars, size, FontStyle.Normal);
            if (System.Array.IndexOf(GlyphSizes, Tuning.FontSize) < 0) // 调参字号一并预热
                runtimeFont.RequestCharactersInTexture(chars, Tuning.FontSize, FontStyle.Normal);
        }

        /// <summary>伤害飘字：技能名 + 数值（暴击更大更红），格挡/闪避/反弹飘对应文案。</summary>
        public void ShowDamage(UnitView unit, string skillName, int amount, bool isCrit,
                               string mitigation = "", string damageType = "physical")
        {
            if (unit == null) return;
            if (!string.IsNullOrEmpty(mitigation))
            {
                string label = mitigation switch
                {
                    "block" => "格挡!",
                    "reflect" => "反弹!",
                    _ => "闪避!",
                };
                Show(unit, $"{skillName} {label}", Tuning.Mitigation, 1.0f);
                return;
            }
            var color = damageType == "magic" ? Tuning.MagicDamage
                      : damageType == "true" ? Tuning.TrueDamage
                                             : Tuning.PhysicalDamage;
            string critMark = isCrit ? " 暴击!" : "";
            Show(unit, $"{skillName} -{amount}{critMark}", color, isCrit ? Tuning.CritScale : 1.0f);
        }

        public void ShowHeal(UnitView unit, string skillName, int amount, bool isCrit)
        {
            if (unit == null) return;
            Show(unit, $"{skillName} +{amount}{(isCrit ? " 暴击!" : "")}",
                Tuning.Heal, isCrit ? Tuning.HealCritScale : 1.0f);
        }

        /// <summary>无伤害/治疗的主动释放：施法者头顶单飘技能名（神使戏言等）。</summary>
        public void ShowSkillName(UnitView unit, string skillName)
        {
            if (unit == null || string.IsNullOrEmpty(skillName)) return;
            Show(unit, skillName, Tuning.AttrUp, 1.05f);
        }

        public void ShowStatus(UnitView unit, string statusName, bool gained)
        {
            if (unit == null) return;
            Show(unit, gained ? $"+{statusName}" : $"-{statusName}",
                gained ? Tuning.StatusGain : Tuning.StatusLose, 0.9f);
        }

        public void ShowAttr(UnitView unit, string attrName, int delta)
        {
            if (unit == null) return;
            Show(unit, $"{attrName}{(delta >= 0 ? "+" : "")}{delta}",
                delta >= 0 ? Tuning.AttrUp : Tuning.AttrDown, 0.85f);
        }

        public void Show(UnitView unit, string text, Color color, float scale = 1f)
        {
            int id = unit.gameObject.GetHashCode(); // Unity 6000.5 弃用 GetInstanceID，这里仅作字典键
            _stackDepth.TryGetValue(id, out int depth);
            _stackDepth[id] = depth + 1;

            var mesh = Rent();
            mesh.text = text;
            mesh.color = color;
            var t = mesh.transform;
            t.position = unit.transform.position
                + new Vector3(0f, 1.55f + depth * Tuning.StackSpacing, -1f);
            t.localScale = Vector3.one * Tuning.BaseScale * scale;

            var record = _recordPool.Count > 0 ? _recordPool.Dequeue() : new ActiveFloat();
            record.Mesh = mesh;
            record.UnitKey = id;
            record.Start = t.position;
            record.Color = color;
            record.Age = 0f;
            _active.Add(record);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var item = _active[i];
                item.Age += dt;
                float p = Mathf.Clamp01(item.Age / Tuning.FloatDuration);
                // OutCubic 上浮 + InQuad 淡出，视觉与旧 DOTween 时间轴一致。
                float move = 1f - Mathf.Pow(1f - p, 3f);
                item.Mesh.transform.position = item.Start + Vector3.up * (Tuning.RiseDistance * move);
                float alpha = 1f - p * p;
                item.Mesh.color = new Color(
                    item.Color.r, item.Color.g, item.Color.b, item.Color.a * alpha);

                if (p < 1f) continue;
                Recycle(item.Mesh);
                if (_stackDepth.TryGetValue(item.UnitKey, out int depth))
                    _stackDepth[item.UnitKey] = Mathf.Max(0, depth - 1);
                _active.RemoveAt(i);
                _recordPool.Enqueue(item);
            }
        }

        public void CancelAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Recycle(_active[i].Mesh);
                _recordPool.Enqueue(_active[i]);
            }
            _active.Clear();
            _stackDepth.Clear();
        }

        // ---------------------------------------------------------- 池

        TextMesh Rent()
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            return CreateMesh();
        }

        TextMesh CreateMesh()
        {
            var go = new GameObject("float_text");
            go.transform.SetParent(transform, false);
            var mesh = go.AddComponent<TextMesh>();
            var font = Tuning.ResolveFont(); // B4：字体/字号走调参 SO
            if (font != null)
            {
                mesh.font = font;
                go.GetComponent<MeshRenderer>().material = font.material;
            }
            mesh.fontSize = Tuning.FontSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            go.GetComponent<MeshRenderer>().sortingOrder = 60;
            return mesh;
        }

        void Recycle(TextMesh mesh)
        {
            mesh.gameObject.SetActive(false);
            _pool.Enqueue(mesh);
        }
    }
}
