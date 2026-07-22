using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 卡边火舌带（减廉价感）：三层（外晕 / 火舌 / 亮芯）+ 宽曲线收束
    // + 双频噪声起伏（高低火舌交错，不像铁丝框）。
    // =========================================================================
    public class FireRimFx : MonoBehaviour
    {
        static Material _sharedMat;
        static AnimationCurve _widthCurve;

        struct Edge
        {
            public LineRenderer Halo;
            public LineRenderer Tongue;
            public LineRenderer Core;
            public Vector3 From;
            public Vector3 To;
            public Vector3 Outward;
            public float Strength;
            public int Seed;
        }

        Edge[] _edges;
        float _phase;

        public static FireRimFx Create(Transform parent, bool fullBorder, float strength)
        {
            var go = new GameObject(fullBorder ? "FireRim_Full" : "FireRim_Foot");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.5f);
            var fx = go.AddComponent<FireRimFx>();
            fx.Build(fullBorder, strength);
            return fx;
        }

        void Build(bool fullBorder, float strength)
        {
            EnsureMat();
            if (_widthCurve == null)
            {
                _widthCurve = new AnimationCurve(
                    new Keyframe(0f, 0.2f),
                    new Keyframe(0.12f, 1f),
                    new Keyframe(0.88f, 1f),
                    new Keyframe(1f, 0.2f));
            }

            const float hw = 0.88f;
            const float hh = 1.18f;
            const float outN = 0.05f;

            if (fullBorder)
            {
                _edges = new[]
                {
                    MakeEdge(new Vector3(-hw, hh + outN, 0f), new Vector3(hw, hh + outN, 0f),
                             Vector3.up, strength, 11),
                    MakeEdge(new Vector3(-hw, -(hh + outN), 0f), new Vector3(hw, -(hh + outN), 0f),
                             Vector3.down, strength, 23),
                    MakeEdge(new Vector3(-(hw + outN), -hh, 0f), new Vector3(-(hw + outN), hh, 0f),
                             Vector3.left, strength, 37),
                    MakeEdge(new Vector3(hw + outN, -hh, 0f), new Vector3(hw + outN, hh, 0f),
                             Vector3.right, strength, 41),
                };
            }
            else
            {
                _edges = new[]
                {
                    MakeEdge(new Vector3(-hw * 0.92f, -(hh + outN), 0f),
                             new Vector3(hw * 0.92f, -(hh + outN), 0f),
                             Vector3.down, strength, 7),
                };
            }
            RefreshPolylines(1f);
        }

        Edge MakeEdge(Vector3 from, Vector3 to, Vector3 outward, float strength, int seed)
        {
            float s = Mathf.Clamp01(strength);
            return new Edge
            {
                From = from,
                To = to,
                Outward = outward.normalized,
                Strength = s,
                Seed = seed,
                Halo = MakeLine("Halo", 0.34f * s + 0.1f, 14),
                Tongue = MakeLine("Tongue", 0.18f * s + 0.06f, 15),
                Core = MakeLine("Core", 0.07f * s + 0.025f, 16),
            };
        }

        LineRenderer MakeLine(string name, float width, int sorting)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _sharedMat;
            lr.useWorldSpace = false;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.widthMultiplier = width;
            lr.widthCurve = _widthCurve;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = sorting;
            return lr;
        }

        static void EnsureMat()
        {
            if (_sharedMat != null) return;
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Unlit/Color");
            _sharedMat = new Material(shader);
        }

        void Update()
        {
            _phase += Time.deltaTime * 7.5f;
            // 慢呼吸 + 快颤，避免整条同闪同灭的廉价感
            float breath = 0.9f + 0.1f * Mathf.Sin(_phase * 0.9f);
            float flutter = 0.96f + 0.04f * Mathf.Sin(_phase * 5.3f);
            RefreshPolylines(breath * flutter);
        }

        void RefreshPolylines(float flicker)
        {
            if (_edges == null) return;
            const int segs = 28;
            for (int e = 0; e < _edges.Length; e++)
            {
                var edge = _edges[e];
                float str = edge.Strength * flicker;

                // 外晕：暗红橙、矮胖、更透
                PaintFire(edge.Halo, segs, edge.From, edge.To, edge.Outward,
                          bulge: 0.14f * str + 0.05f,
                          start: new Color(1f, 0.25f, 0.02f, 0.28f * str),
                          end: new Color(0.8f, 0.08f, 0f, 0.05f * str),
                          seed: edge.Seed,
                          detail: 2.2f, speed: 0.22f);

                // 火舌：主橙黄
                PaintFire(edge.Tongue, segs, edge.From, edge.To, edge.Outward,
                          bulge: 0.1f * str + 0.035f,
                          start: new Color(1f, 0.55f, 0.12f, 0.7f * str),
                          end: new Color(1f, 0.22f, 0.02f, 0.2f * str),
                          seed: edge.Seed + 5,
                          detail: 4.8f, speed: 0.4f);

                // 亮芯：贴边白黄
                PaintFire(edge.Core, segs, edge.From, edge.To, edge.Outward,
                          bulge: 0.035f * str + 0.012f,
                          start: new Color(1f, 0.95f, 0.7f, 0.85f * str),
                          end: new Color(1f, 0.7f, 0.25f, 0.35f * str),
                          seed: edge.Seed + 11,
                          detail: 6.5f, speed: 0.55f);
            }
        }

        void PaintFire(LineRenderer lr, int segs, Vector3 from, Vector3 to, Vector3 outward,
                       float bulge, Color start, Color end, int seed, float detail, float speed)
        {
            lr.positionCount = segs + 1;
            // 沿边渐变：两端更暗更透，中间略亮
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(start, 0f),
                    new GradientColorKey(Color.Lerp(start, end, 0.5f), 0.5f),
                    new GradientColorKey(end, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(start.a * 0.35f, 0f),
                    new GradientAlphaKey(Mathf.Max(start.a, end.a), 0.5f),
                    new GradientAlphaKey(end.a * 0.35f, 1f),
                });
            lr.colorGradient = grad;

            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                var p = Vector3.Lerp(from, to, t);
                float n1 = Mathf.PerlinNoise(seed * 0.13f + t * detail, _phase * speed);
                float n2 = Mathf.PerlinNoise(seed * 0.29f + t * (detail * 2.1f), _phase * speed * 1.7f + 3f);
                // 高低火舌交错：低频大浪 + 高频毛刺
                float tip = (0.45f + 0.4f * n1 + 0.25f * n2) * bulge;
                tip *= 0.25f + 0.75f * Mathf.Sin(t * Mathf.PI); // 两端收
                lr.SetPosition(i, p + outward * tip);
            }
        }
    }
}
