using System.Collections;
using ClientBattle.VFX;
using DigitalRuby.LightningBolt;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 宙斯雷霆常驻：Digital Ruby LightningBolt + 卡面多向乱劈调度
    // - 边→异边 / 对角 / 短弧 / 偶发竖劈；透明度 0.7~0.9；频率偏低
    // 触发贯穿对面见 RemoteStrike（透明度 0.2）
    // =========================================================================
    public class ThunderAuraDriver : MonoBehaviour
    {
        const int PoolSize = 5;

        const float HalfW = 0.78f;
        const float HalfH = 1.05f;

        LightningBoltScript[] _pool;
        Coroutine _loop;

        void OnEnable()
        {
            EnsurePool();
            if (_loop != null) StopCoroutine(_loop);
            _loop = StartCoroutine(StrikeLoop());
        }

        void OnDisable()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }
        }

        void EnsurePool()
        {
            if (_pool != null) return;
            _pool = new LightningBoltScript[PoolSize];
            for (int i = 0; i < PoolSize; i++)
                _pool[i] = DrLightningUtil.Spawn(transform, $"Bolt_{i}");
        }

        IEnumerator StrikeLoop()
        {
            yield return new WaitForSeconds(Random.Range(0f, 0.35f));
            int cursor = 0;
            while (enabled)
            {
                int count = Random.value < 0.32f ? 2 : 1;
                for (int n = 0; n < count; n++)
                {
                    var bolt = _pool[cursor % PoolSize];
                    cursor++;
                    StrikeVariety(bolt);
                }
                float gap = Random.value < 0.18f
                    ? Random.Range(0.38f, 0.62f)
                    : Random.Range(0.14f, 0.32f);
                yield return new WaitForSeconds(gap);
            }
        }

        void StrikeVariety(LightningBoltScript bolt)
        {
            float roll = Random.value;
            Vector3 from, to;
            float chaos;
            float life;
            int gen;

            if (roll < 0.38f)
            {
                PickTwoEdges(out from, out to);
                chaos = Random.Range(0.12f, 0.2f);
                life = Random.Range(0.08f, 0.14f);
                gen = 5;
            }
            else if (roll < 0.62f)
            {
                from = RandomCorner();
                to = OppositeCorner(from);
                to += (Vector3)(Random.insideUnitCircle * 0.12f);
                chaos = Random.Range(0.14f, 0.22f);
                life = Random.Range(0.09f, 0.15f);
                gen = 6;
            }
            else if (roll < 0.82f)
            {
                from = RandomOnPerimeter();
                to = from + (Vector3)(Random.insideUnitCircle.normalized
                                      * Random.Range(0.35f, 0.75f));
                to = ClampToCard(to);
                chaos = Random.Range(0.08f, 0.14f);
                life = Random.Range(0.06f, 0.11f);
                gen = 4;
            }
            else
            {
                float x = Random.Range(-HalfW * 0.85f, HalfW * 0.85f);
                from = new Vector3(x, HalfH, 0f);
                to = new Vector3(x + Random.Range(-0.2f, 0.2f), -HalfH * Random.Range(0.3f, 1f), 0f);
                chaos = Random.Range(0.1f, 0.18f);
                life = Random.Range(0.08f, 0.13f);
                gen = 5;
            }

            DrLightningUtil.Fire(
                bolt,
                transform.TransformPoint(from),
                transform.TransformPoint(to),
                duration: life,
                chaos: chaos,
                generations: gen,
                alpha: Random.Range(0.7f, 0.9f),
                widthMul: Random.Range(0.32f, 0.48f),
                sortingOrder: 15);
        }

        void PickTwoEdges(out Vector3 a, out Vector3 b)
        {
            int e0 = Random.Range(0, 4);
            int e1 = (e0 + Random.Range(1, 4)) % 4;
            a = PointOnEdge(e0, Random.value);
            b = PointOnEdge(e1, Random.value);
        }

        static Vector3 PointOnEdge(int edge, float t)
        {
            return edge switch
            {
                0 => new Vector3(Mathf.Lerp(-HalfW, HalfW, t), HalfH, 0f),
                1 => new Vector3(HalfW, Mathf.Lerp(-HalfH, HalfH, t), 0f),
                2 => new Vector3(Mathf.Lerp(-HalfW, HalfW, t), -HalfH, 0f),
                _ => new Vector3(-HalfW, Mathf.Lerp(-HalfH, HalfH, t), 0f),
            };
        }

        Vector3 RandomOnPerimeter() => PointOnEdge(Random.Range(0, 4), Random.value);

        Vector3 RandomCorner()
        {
            float x = Random.value < 0.5f ? -HalfW : HalfW;
            float y = Random.value < 0.5f ? -HalfH : HalfH;
            return new Vector3(x, y, 0f);
        }

        static Vector3 OppositeCorner(Vector3 c)
            => new Vector3(-Mathf.Sign(c.x) * HalfW, -Mathf.Sign(c.y) * HalfH, 0f);

        static Vector3 ClampToCard(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -HalfW, HalfW);
            p.y = Mathf.Clamp(p.y, -HalfH, HalfH);
            return p;
        }
    }
}
