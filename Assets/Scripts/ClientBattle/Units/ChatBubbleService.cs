using System.Collections.Generic;
using ClientBattle.Placeholder;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 台词气泡服务（client_perform：性格事件推送即播，
    // 通过武将卡牌弹出聊天框的形式把台词播出来）。
    // 气泡 = 圆角底板 Sprite + TextMesh；同一单位多条台词排队不叠字。
    // 底板资源：Resources/ClientBattle/UI/chat_bubble.png → 白色占位。
    // =========================================================================

    public class ChatBubbleService : MonoBehaviour
    {
        public static ChatBubbleService Instance { get; private set; }

        readonly Queue<GameObject> _pool = new();
        readonly Dictionary<int, float> _busyUntil = new(); // unit id → 排队时间戳

        const float BubbleDuration = 1.6f;

        public static ChatBubbleService Ensure()
        {
            if (Instance == null)
            {
                var go = new GameObject("ChatBubbleService");
                Instance = go.AddComponent<ChatBubbleService>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>开战前预建气泡对象与底图，首次台词不在战斗中创建 GameObject/纹理。</summary>
        public void Prewarm(int count = 4)
        {
            int missing = count - _pool.Count;
            for (int i = 0; i < missing; i++)
            {
                var bubble = Build();
                bubble.SetActive(false);
                _pool.Enqueue(bubble);
            }
        }

        /// <summary>弹台词气泡；空台词只做性格发作提示（飘性格名由调用方处理）。</summary>
        public void Say(UnitView unit, string line)
        {
            if (unit == null || string.IsNullOrEmpty(line)) return;

            // 同一单位台词密集时向后排队，避免气泡互相覆盖
            int id = unit.gameObject.GetHashCode(); // Unity 6000.5 弃用 GetInstanceID，这里仅作字典键
            float now = Time.time;
            float startAt = _busyUntil.TryGetValue(id, out var busy) && busy > now ? busy : now;
            _busyUntil[id] = startAt + BubbleDuration * 0.7f;

            var bubble = Rent();
            bubble.SetActive(false);
            DOVirtual.DelayedCall(startAt - now, () => Pop(bubble, unit, line), true)
                .SetLink(bubble);
        }

        void Pop(GameObject bubble, UnitView unit, string line)
        {
            if (unit == null) { Recycle(bubble); return; }
            bubble.SetActive(true);
            bubble.transform.position = unit.BubbleAnchor.position + new Vector3(0f, 0f, -1f);
            bubble.transform.localScale = Vector3.zero;

            var text = bubble.GetComponentInChildren<TextMesh>();
            text.text = Wrap(line, 9);

            // 底板按文字行数拉伸
            var back = bubble.transform.Find("Back").GetComponent<SpriteRenderer>();
            int lines = text.text.Split('\n').Length;
            int width = Mathf.Min(line.Length, 9);
            back.transform.localScale = new Vector3(0.28f * width + 0.5f, 0.42f * lines + 0.25f, 1f);

            DOTween.Sequence()
                .Append(bubble.transform.DOScale(1f, 0.18f).SetEase(Ease.OutBack))
                .AppendInterval(BubbleDuration)
                .Append(bubble.transform.DOScale(0f, 0.15f).SetEase(Ease.InBack))
                .OnComplete(() => Recycle(bubble))
                .SetLink(bubble);
        }

        static string Wrap(string line, int perLine)
        {
            if (line.Length <= perLine) return line;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i += perLine)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(line.Substring(i, Mathf.Min(perLine, line.Length - i)));
            }
            return sb.ToString();
        }

        public void CancelAll()
        {
            foreach (Transform child in transform)
            {
                child.DOKill();
                if (child.gameObject.activeSelf) Recycle(child.gameObject);
            }
            _busyUntil.Clear();
        }

        // ---------------------------------------------------------- 池

        GameObject Rent()
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                pooled.SetActive(true);
                return pooled;
            }
            return Build();
        }

        GameObject Build()
        {
            var go = new GameObject("chat_bubble");
            go.transform.SetParent(transform, false);

            var back = new GameObject("Back");
            back.transform.SetParent(go.transform, false);
            var renderer = back.AddComponent<SpriteRenderer>();
            renderer.sprite = PlaceholderFactory.GetSprite(
                "UI", "chat_bubble", new Color(1f, 1f, 0.96f, 0.95f), 64);
            renderer.sortingOrder = 70;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localScale = Vector3.one * 0.07f;
            var mesh = textGo.AddComponent<TextMesh>();
            mesh.fontSize = 44;
            mesh.color = new Color(0.15f, 0.12f, 0.1f);
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            textGo.GetComponent<MeshRenderer>().sortingOrder = 71;
            return go;
        }

        void Recycle(GameObject bubble)
        {
            bubble.SetActive(false);
            _pool.Enqueue(bubble);
        }
    }
}
