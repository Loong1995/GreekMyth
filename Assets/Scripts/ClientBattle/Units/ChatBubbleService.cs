using System.Collections.Generic;
using ClientBattle.Placeholder;
using DG.Tweening;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 台词气泡：TraitLine 播放单元独占时间轴时走 SayExclusive（即时弹出、
    // 返回应阻塞的秒数）；与气泡动画时长一致，播完立刻接下单元、禁止重叠。
    // =========================================================================

    public class ChatBubbleService : MonoBehaviour
    {
        public static ChatBubbleService Instance { get; private set; }

        readonly Queue<GameObject> _pool = new();

        /// <summary>独占台词单元的可见时长（弹出+停留+收起），与时间轴 Wait 对齐。</summary>
        public const float AppearSeconds = 0.12f;
        public const float HoldSeconds = 0.9f;
        public const float DisappearSeconds = 0.12f;
        public static float ExclusiveSeconds => AppearSeconds + HoldSeconds + DisappearSeconds;

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

        /// <summary>独占播放单元用：立刻弹气泡，返回时间轴应阻塞的秒数（未乘 DurationMul）。
        /// 同卡若已有气泡先杀掉，保证不叠字、不排队延迟。</summary>
        public float SayExclusive(UnitView unit, string line)
        {
            if (unit == null || string.IsNullOrEmpty(line)) return 0f;
            CancelBubblesOn(unit);
            var bubble = Rent();
            Pop(bubble, unit, line);
            return ExclusiveSeconds;
        }

        /// <summary>非时间轴调用（遗留）；等价 SayExclusive，调用方若不等待会与后续演出重叠。</summary>
        public void Say(UnitView unit, string line) => SayExclusive(unit, line);

        public void CancelAll()
        {
            foreach (Transform child in transform)
            {
                child.DOKill();
                if (child.gameObject.activeSelf) Recycle(child.gameObject);
            }
        }

        void CancelBubblesOn(UnitView unit)
        {
            if (unit == null) return;
            var anchor = unit.BubbleAnchor;
            foreach (Transform child in transform)
            {
                if (!child.gameObject.activeSelf) continue;
                // 近似：正在该卡附近的气泡清掉（独占重弹）
                if (anchor != null &&
                    (child.position - anchor.position).sqrMagnitude < 2.5f)
                {
                    child.DOKill();
                    Recycle(child.gameObject);
                }
            }
        }

        void Pop(GameObject bubble, UnitView unit, string line)
        {
            if (unit == null) { Recycle(bubble); return; }
            bubble.SetActive(true);
            bubble.transform.position = unit.BubbleAnchor.position + new Vector3(0f, 0f, -1f);
            bubble.transform.localScale = Vector3.zero;

            var text = bubble.GetComponentInChildren<TextMesh>();
            text.text = Wrap(line, 9);

            var back = bubble.transform.Find("Back").GetComponent<SpriteRenderer>();
            int lines = text.text.Split('\n').Length;
            int width = Mathf.Min(line.Length, 9);
            back.transform.localScale = new Vector3(0.28f * width + 0.5f, 0.42f * lines + 0.25f, 1f);

            DOTween.Sequence()
                .Append(bubble.transform.DOScale(1f, AppearSeconds).SetEase(Ease.OutBack))
                .AppendInterval(HoldSeconds)
                .Append(bubble.transform.DOScale(0f, DisappearSeconds).SetEase(Ease.InBack))
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
