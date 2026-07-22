using System.Collections;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 程序化折线闪电（减廉价感）：三层（柔光晕 / 中辉 / 白芯）+ 端点收束
    // + 位移相关折线（不像均匀锯齿铁丝）。
    // =========================================================================
    public class LightningBoltFx : MonoBehaviour
    {
        const int Segments = 18;
        static Material _sharedMat;
        static readonly Vector3[] Scratch = new Vector3[Segments + 1];
        static AnimationCurve _taper;

        LineRenderer _halo;
        LineRenderer _glow;
        LineRenderer _core;
        Coroutine _fade;

        // Fade 基色
        Color _h0, _h1, _g0, _g1, _c0, _c1;

        public static LightningBoltFx Create(Transform parent, string name = "LightningBolt")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<LightningBoltFx>();
            fx.Build();
            go.SetActive(false);
            return fx;
        }

        void Build()
        {
            EnsureMat();
            if (_taper == null)
            {
                _taper = new AnimationCurve(
                    new Keyframe(0f, 0.15f),
                    new Keyframe(0.15f, 1f),
                    new Keyframe(0.85f, 1f),
                    new Keyframe(1f, 0.12f));
            }
            _halo = MakeLine("Halo", 0.28f, 14);
            _glow = MakeLine("Glow", 0.12f, 15);
            _core = MakeLine("Core", 0.035f, 16);
            ApplyTaper(_halo);
            ApplyTaper(_glow);
            ApplyTaper(_core);
        }

        static void EnsureMat()
        {
            if (_sharedMat != null) return;
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Unlit/Color");
            _sharedMat = new Material(shader);
        }

        static void ApplyTaper(LineRenderer lr)
        {
            lr.widthCurve = _taper;
        }

        LineRenderer MakeLine(string name, float width, int sorting)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _sharedMat;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.useWorldSpace = false;
            lr.positionCount = 0;
            lr.widthMultiplier = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = sorting;
            return lr;
        }

        public void StrikeLocal(Vector3 from, Vector3 to, float duration = 0.18f,
                                float jag = 0.12f, bool allowBranch = true, float alphaMul = 1f)
        {
            if (_halo == null) Build();
            alphaMul = Mathf.Clamp01(alphaMul);
            SetSpace(world: false);
            _halo.widthMultiplier = 0.32f;
            _glow.widthMultiplier = 0.13f;
            _core.widthMultiplier = 0.038f;
            gameObject.SetActive(true);
            FillPolyline(from, to, jag);
            WriteAll(local: true);
            SetColors(alphaMul, punch: true);
            RestartFade(duration);

            if (allowBranch && Random.value < 0.35f)
                StartCoroutine(FlashBranch(from, to, duration * 0.6f, alphaMul));
        }

        public void StrikeWorld(Vector3 from, Vector3 to, float duration = 0.3f,
                                float jag = 0.2f, float alphaMul = 0.2f)
        {
            if (_halo == null) Build();
            alphaMul = Mathf.Clamp01(alphaMul);
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SetSpace(world: true);
            _halo.widthMultiplier = 0.42f;
            _glow.widthMultiplier = 0.18f;
            _core.widthMultiplier = 0.055f;
            gameObject.SetActive(true);
            FillPolyline(from, to, jag);
            WriteAll(local: false);
            SetColors(alphaMul, punch: true);
            RestartFade(duration);
        }

        void SetSpace(bool world)
        {
            _halo.useWorldSpace = world;
            _glow.useWorldSpace = world;
            _core.useWorldSpace = world;
        }

        void SetColors(float a, bool punch)
        {
            float p = punch ? 1.15f : 1f; // 起闪略亮一截再淡
            _h0 = new Color(0.35f, 0.65f, 1f, 0.22f * a * p);
            _h1 = new Color(0.2f, 0.4f, 0.95f, 0.06f * a * p);
            _g0 = new Color(0.55f, 0.85f, 1f, 0.55f * a * p);
            _g1 = new Color(0.35f, 0.6f, 1f, 0.18f * a * p);
            _c0 = new Color(1f, 1f, 1f, 0.95f * a * p);
            _c1 = new Color(0.85f, 0.95f, 1f, 0.75f * a * p);
            Paint(_halo, _h0, _h1);
            Paint(_glow, _g0, _g1);
            Paint(_core, _c0, _c1);
        }

        /// <summary>位移相关折线：相邻段偏移相关，避免廉价均匀锯齿。</summary>
        static void FillPolyline(Vector3 from, Vector3 to, float jag)
        {
            var delta = to - from;
            var dir = delta.sqrMagnitude > 1e-8f ? delta.normalized : Vector3.down;
            var side = Vector3.Cross(dir, Vector3.forward);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            float len = Mathf.Max(0.35f, delta.magnitude);
            Scratch[0] = from;
            Scratch[Segments] = to;
            float carry = 0f;
            for (int i = 1; i < Segments; i++)
            {
                float t = i / (float)Segments;
                float envelope = Mathf.Sin(t * Mathf.PI);
                // 70% 继承上段偏移 + 30% 新噪声 → 更像真闪电折线
                float kick = (Random.value * 2f - 1f) * jag * len * 0.28f;
                carry = carry * 0.55f + kick * 0.45f;
                // 偶发大折
                if (Random.value < 0.12f)
                    carry += (Random.value * 2f - 1f) * jag * len * 0.22f;
                Scratch[i] = Vector3.Lerp(from, to, t) + side * (carry * envelope);
            }
        }

        void WriteAll(bool local)
        {
            Write(_halo);
            Write(_glow);
            Write(_core);
        }

        void Write(LineRenderer lr)
        {
            lr.positionCount = Segments + 1;
            for (int i = 0; i <= Segments; i++)
                lr.SetPosition(i, Scratch[i]);
        }

        static void Paint(LineRenderer lr, Color start, Color end)
        {
            lr.startColor = start;
            lr.endColor = end;
        }

        void RestartFade(float duration)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeOut(duration));
        }

        IEnumerator FlashBranch(Vector3 mainFrom, Vector3 mainTo, float duration, float alphaMul)
        {
            int mid = Mathf.Clamp(Segments / 2 + Random.Range(-2, 3), 2, Segments - 2);
            var forkFrom = Scratch[mid];
            var dir = (mainTo - mainFrom).normalized;
            var side = Vector3.Cross(dir, Vector3.forward).normalized
                       * (Random.value < 0.5f ? 1f : -1f);
            var forkTo = forkFrom + dir * Random.Range(0.18f, 0.4f)
                         + side * Random.Range(0.1f, 0.28f);
            var branch = Create(transform, "Branch");
            branch.StrikeLocal(forkFrom, forkTo, duration, jag: 0.06f,
                               allowBranch: false, alphaMul: alphaMul * 0.75f);
            branch._halo.widthMultiplier = 0.16f;
            branch._glow.widthMultiplier = 0.07f;
            branch._core.widthMultiplier = 0.022f;
            yield return new WaitForSeconds(duration + 0.02f);
            if (branch != null) Destroy(branch.gameObject);
        }

        IEnumerator FadeOut(float duration)
        {
            // 前 20% 保持起闪，后段柔退
            float hold = duration * 0.22f;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float a = t < hold ? 1f
                    : Mathf.SmoothStep(1f, 0f, (t - hold) / Mathf.Max(0.01f, duration - hold));
                Paint(_halo, MulA(_h0, a), MulA(_h1, a));
                Paint(_glow, MulA(_g0, a), MulA(_g1, a));
                Paint(_core, MulA(_c0, a), MulA(_c1, a));
                yield return null;
            }
            gameObject.SetActive(false);
            _fade = null;
        }

        static Color MulA(Color c, float a)
        {
            c.a *= a;
            return c;
        }
    }
}
