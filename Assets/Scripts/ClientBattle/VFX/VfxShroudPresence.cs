using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【绕身通用】显隐与「是否算有罩」的唯一实现。
    //
    // - SetShown / Show / Hide：任意时机驱动渐显渐隐（回合/技能/手动皆可）。
    // - IsPresent：视觉上仍有罩（含渐隐途中）；渐隐收干净后为 false → 受击恢复抖动。
    // - 基色只锁一次；禁止在再显时把压暗色写回 base（P-60）。
    // - 个案不要再写平行 Pulse；时机策略见 StatusPresentation.ShroudVisibility。
    // =========================================================================

    public sealed class VfxShroudPresence : MonoBehaviour
    {
        public float FadeSeconds = 0.55f;

        VfxShroudFollower _follower;
        float _target;
        float _current = -1f;
        bool _basesLocked;

        ParticleSystem[] _particles;
        Color[] _baseStartColor;
        float[] _baseEmission;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;
        Color[] _baseMatColor;
        int[] _colorProp; // 0=无 1=_Color 2=_BaseColor 3=_TintColor 4=_FresnelColor

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        static readonly int FresnelColorId = Shader.PropertyToID("_FresnelColor");

        /// <summary>视觉上仍算有绕身（渐隐途中仍为 true；收干净后 false）。
        /// 受击抖动闸门只看本属性，不看状态是否仍挂着。</summary>
        public bool IsPresent => _current > 0.001f;

        /// <summary>目标是否要显（可与 IsPresent 不同：正在渐隐时 WantShown=false 且仍 Present）。</summary>
        public bool WantShown => _target > 0.5f;

        public void Bind(VfxShroudFollower follower)
        {
            _follower = follower;
            _basesLocked = false;
            Cache(lockBases: true);
        }

        Transform Cell => _follower != null ? _follower.Cell : null;

        public void Show(bool instant = false) => SetShown(true, instant);
        public void Hide(bool instant = false) => SetShown(false, instant);

        public void SetShown(bool shown, bool instant = false)
        {
            _target = shown ? 1f : 0f;
            if (instant)
            {
                _current = _target;
                Apply(_current);
            }
            else if (_current < 0f)
            {
                // 尚未 Apply 过：从对面起，避免 _current=-1 卡死
                _current = shown ? 0f : 1f;
            }
        }

        void Cache(bool lockBases)
        {
            var cell = Cell;
            if (cell == null) return;

            _particles = cell.GetComponentsInChildren<ParticleSystem>(true);
            _renderers = cell.GetComponentsInChildren<Renderer>(true);
            if (lockBases || !_basesLocked)
            {
                _baseStartColor = new Color[_particles.Length];
                _baseEmission = new float[_particles.Length];
                for (int i = 0; i < _particles.Length; i++)
                {
                    _baseStartColor[i] = _particles[i].main.startColor.color;
                    _baseEmission[i] = _particles[i].emission.rateOverTime.constant;
                }

                _baseMatColor = new Color[_renderers.Length];
                _colorProp = new int[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var mat = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                    if (mat == null) continue;
                    if (mat.HasProperty(ColorId))
                    {
                        _colorProp[i] = 1;
                        _baseMatColor[i] = mat.GetColor(ColorId);
                    }
                    else if (mat.HasProperty(BaseColorId))
                    {
                        _colorProp[i] = 2;
                        _baseMatColor[i] = mat.GetColor(BaseColorId);
                    }
                    else if (mat.HasProperty(TintColorId))
                    {
                        _colorProp[i] = 3;
                        _baseMatColor[i] = mat.GetColor(TintColorId);
                    }
                    else if (mat.HasProperty(FresnelColorId))
                    {
                        _colorProp[i] = 4;
                        _baseMatColor[i] = mat.GetColor(FresnelColorId);
                    }
                }
                _basesLocked = true;
            }
            _block = new MaterialPropertyBlock();
        }

        void RestoreBases()
        {
            if (_particles != null && _baseStartColor != null)
                for (int i = 0; i < _particles.Length; i++)
                {
                    if (_particles[i] == null) continue;
                    var main = _particles[i].main;
                    main.startColor = _baseStartColor[i];
                    var emission = _particles[i].emission;
                    emission.rateOverTime = _baseEmission[i];
                }
            if (_renderers != null)
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] == null) continue;
                    _renderers[i].SetPropertyBlock(null);
                    _renderers[i].enabled = true;
                }
        }

        void Update()
        {
            if (_current < 0f) return;
            if (Mathf.Approximately(_current, _target)) return;
            float step = FadeSeconds <= 0.01f ? 1f : Time.deltaTime / FadeSeconds;
            _current = Mathf.MoveTowards(_current, _target, step);
            Apply(_current);
        }

        void Apply(float t)
        {
            if (_particles == null) Cache(lockBases: true);
            var cell = Cell;
            t = Mathf.Clamp01(t);

            if (t <= 0.001f)
            {
                RestoreBases();
                if (_particles != null)
                    for (int i = 0; i < _particles.Length; i++)
                    {
                        if (_particles[i] == null) continue;
                        _particles[i].Clear(true);
                        _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                if (_renderers != null)
                    for (int i = 0; i < _renderers.Length; i++)
                        if (_renderers[i] != null) _renderers[i].enabled = false;
                if (cell != null) cell.gameObject.SetActive(false);
                return;
            }

            if (cell != null && !cell.gameObject.activeSelf)
            {
                cell.gameObject.SetActive(true);
                Cache(lockBases: false);
                RestoreBases();
            }

            if (_renderers != null)
                for (int i = 0; i < _renderers.Length; i++)
                    if (_renderers[i] != null) _renderers[i].enabled = true;

            if (_particles != null)
                for (int i = 0; i < _particles.Length; i++)
                {
                    var ps = _particles[i];
                    if (ps == null) continue;
                    var main = ps.main;
                    var c = _baseStartColor[i];
                    c.a *= t;
                    main.startColor = c;
                    var emission = ps.emission;
                    emission.rateOverTime = _baseEmission[i] * t;
                    if (!ps.isPlaying) ps.Play(true);
                }

            if (t >= 0.999f)
            {
                RestoreBases();
                return;
            }

            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null || _colorProp[i] == 0) continue;
                var c = _baseMatColor[i];
                c.a *= t;
                if (_colorProp[i] == 4)
                    c = _baseMatColor[i] * t;
                r.GetPropertyBlock(_block);
                if (_colorProp[i] == 1) _block.SetColor(ColorId, c);
                else if (_colorProp[i] == 2) _block.SetColor(BaseColorId, c);
                else if (_colorProp[i] == 3) _block.SetColor(TintColorId, c);
                else _block.SetColor(FresnelColorId, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
