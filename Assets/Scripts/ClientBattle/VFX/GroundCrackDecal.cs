using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 地面裂纹贴花（L1 裂缝 + L2 缝底）的淡入/驻留/淡出。
    //
    // 为什么不用 RFX4/Magic 自带的投影贴花：它们是 Built-in 管线的屏幕空间
    // 深度投影 shader，在 URP 下不做深度重建，会把投影盒渲成悬空品红亮块
    // （2026-07-25 实测，见 ai_workflow_pitfalls P-32/P-33）。所以裂纹改为
    // 平躺的 SpriteRenderer 面片，压在地面网格之上、卡牌之下。
    //
    // 挂在**组节点**上（组节点已绕 X 转 90° 平躺），驱动其子树下所有
    // SpriteRenderer：各自保留 prefab 里的基色（L1/L2 亮度不同），只统一推 alpha。
    // 生命周期由 OnEnable 驱动，配合 VFXManager 池化复用（出池 SetActive(true)
    // 即重新起一遍淡入），不依赖调用方传生命期。
    // =========================================================================

    public class GroundCrackDecal : MonoBehaviour
    {
        public float FadeIn = 0.08f;
        public float Hold = 0.9f;
        public float FadeOut = 0.5f;
        /// <summary>裂纹峰值不透明度。遮罩本身是细线镂空，压到 1 才在亮色
        /// 大理石地面上读得出来；观感靠基色压近黑而非靠半透明。</summary>
        [Range(0f, 1f)] public float PeakAlpha = 1f;
        /// <summary>每次出场随机绕地面法线转一圈，避免同一战斗里裂纹图案复读。
        /// 弹道档必须关掉——它的朝向由调用方按弹道方向给定。</summary>
        public bool RandomizeSpin = true;

        /// <summary>prefab 烘制时子面片的世界尺寸（组合器写入），
        /// 供 CardWidthFactor 反算缩放。</summary>
        public float BakedSize = 1f;
        /// <summary>&gt;0 时出场按「卡牌宽度 × 本系数 × <see cref="AreaFactor"/>」
        /// 重定直径（命中类骨架）。卡宽随分辨率自适配算出，编辑期拿不到，
        /// 只能运行期定。只缩裂缝组，不动碎块粒子。</summary>
        public float CardWidthFactor;
        /// <summary>面积倍率：命中类在「卡宽 ×1.5」的默认大小上再乘这个数。
        /// 场心大裂地就是同一骨架配大面积（由 `GroundCrackService` 出场写入）。</summary>
        public float AreaFactor = 1f;

        /// <summary>裂缝推进到满所需时间。裂开这个动作本身要被看见，
        /// 所以它独立于 FadeIn（FadeIn 只管整体透明度起步）。</summary>
        public float GrowTime = 0.3f;
        /// <summary>0=从中心向外扩（命中档），1=沿 +u 推进（弹道档沿弹道方向）。</summary>
        public float GrowthMode;
        // ------------------------------------------------ 强度档写入的表现参数
        //
        // 缝宽/持续/亮度都属**强度档**（GroundCrackPalette.StrengthSpec），
        // 由 ApplyStrength 出场时整组写入。别在这里逐项手调，否则三档台阶就散了。

        /// <summary>缝宽增益（`_MaskGain`）：抬遮罩 alpha，细线变粗。</summary>
        public float MaskGain = 1f;
        /// <summary>熔岩锋面峰值强度。HDR 值，靠 BattlePostFx 的 Bloom 过曝成光。</summary>
        public float GlowPeak = 3.2f;
        /// <summary>锋面亮带宽度（`_FrontWidth`）。档位越高越宽＝整条缝在烧。</summary>
        public float FrontWidth = 0.18f;
        /// <summary>锋面扫后缝底的余烬保底（`_EmberFloor`）。</summary>
        public float EmberFloor = 0.25f;
        /// <summary>骨架放大倍率（强度档写入）：高档同一条缝裂得更远，
        /// 而不是原地更亮 —— 只堆亮度会读成一块独立的发光贴图。</summary>
        public float SizeScale = 1f;
        // ------------------------------------------------ 熔岩的独立时间轴
        //
        // 熔岩**故意不与骨架同步**（2026-07-26 人工验收）：缝先黑着裂开，
        // 火晚一步顺着缝爬进去、爬的同时就开始熄，并且在裂缝淡完之前先灭。
        // 同步时读起来像「一张会亮的贴图」，错开才有「裂开→烧起来→冷掉」的过程。

        /// <summary>熔岩起步延迟，单位＝GrowTime 的倍数。要跟着骨架长，
        /// 只晚一小步（缝先黑着裂开，火立刻顺着缝爬）。</summary>
        public float LavaDelay = 0.12f;
        /// <summary>熔岩自身推进时长，单位＝GrowTime 的倍数。≈1＝几乎跟着骨架走；
        /// 过大就会整条缝长完火才亮，读不出「随着骨架生成」。</summary>
        public float LavaGrowMul = 1.15f;
        /// <summary>熔岩寿命占裂缝总时长（FadeIn+Hold+FadeOut）的比例，
        /// &lt;1 ＝比裂缝先灭。</summary>
        [Range(0.2f, 1f)] public float LavaLifeRatio = 0.65f;

        /// <summary>按强度档整组写入缝宽/持续/亮度。裂地件自带三档，
        /// 用哪档由场景决定（模式默认 + 英雄战法专配），
        /// 所以同一个 prefab 三档通吃，不为分档另烘变体。</summary>
        public void ApplyStrength(GroundCrackPalette.Strength strength,
                                  GroundCrackPalette.Mode mode = GroundCrackPalette.Mode.Path)
        {
            var spec = GroundCrackPalette.SpecOf(strength, mode);
            MaskGain = spec.MaskGain;
            Hold = spec.Hold;
            FadeOut = spec.FadeOut;
            GlowPeak = spec.GlowPeak;
            FrontWidth = spec.FrontWidth;
            EmberFloor = spec.EmberFloor;
            SizeScale = spec.SizeScale;
            ApplyCardWidth();
        }

        /// <summary>设面积倍率并立即重算尺寸（命中类；弹道类无 CardWidthFactor 时无效）。</summary>
        public void ApplyArea(float area)
        {
            AreaFactor = Mathf.Max(0.01f, area);
            ApplyCardWidth();
        }

        /// <summary>播放时长倍率（=VFXContext.Scaled(1)）。上面几个时长都是"常速秒"，
        /// 而实例存活时长由调用方按倍速换算过；不同步换算的话，4 倍速下贴花刚淡入
        /// 就被回池，观感等于没播。由 GroundCrackService 每次出场写入。</summary>
        public float DurationScale = 1f;

        static readonly int GrowthId = Shader.PropertyToID("_Growth");
        static readonly int GrowthModeId = Shader.PropertyToID("_GrowthMode");
        static readonly int GlowId = Shader.PropertyToID("_GlowIntensity");
        static readonly int FrontWidthId = Shader.PropertyToID("_FrontWidth");
        static readonly int EmberFloorId = Shader.PropertyToID("_EmberFloor");
        static readonly int MaskGainId = Shader.PropertyToID("_MaskGain");
        static readonly int GlowGrowthId = Shader.PropertyToID("_GlowGrowth");
        static readonly int LavaExtinguishId = Shader.PropertyToID("_LavaExtinguish");
        static readonly int LavaFadeSeedId = Shader.PropertyToID("_LavaFadeSeed");

        SpriteRenderer[] _renderers;
        Color[] _baseColors;
        float[] _glowScale;
        float[] _growLag;
        float[] _lavaLag;
        MaterialPropertyBlock _block;
        float _elapsed;

        // ---------------------------------------------- 每次出场现摇的随机化
        //
        // 三档参数只定「台阶」，具体这一发裂多快、火晚多少、烧多久要**每发不同**：
        // 全场同参数时，一次群攻的 3~5 处裂地是同一段动画的复读，读作僵硬的机关，
        // 而不是地面被打裂（2026-07-26 人工验收）。抖动幅度不改台阶次序。
        float _growMul = 1f;     // 本发裂缝推进快慢
        float _lavaDelayMul = 1f;// 本发熔岩起步早晚
        float _lavaGrowMul = 1f; // 本发熔岩爬行快慢
        float _lavaLifeMul = 1f; // 本发熔岩寿命长短
        float _holdMul = 1f;     // 本发裂痕停留长短（上限压在实例存活时长内）
        float _startDelay;       // 本发整体错峰（秒，常速）
        float _burstPhase;       // 推进"一阵一顿"的相位
        float _flickerPhase;     // 熔岩明灭相位
        float _lavaFadeSeed;     // 本发灭点噪声种子（每发不同，全局才不会齐灭）

        void Roll()
        {
            // 区间放宽到「两发之间一眼能看出快慢不同」：一次群攻同时落 3~5 处，
            // 窄区间（±20%）在全局一看仍是同一个节奏（2026-07-26 二次打回）
            _growMul = Random.Range(0.55f, 1.9f);
            // 生长要紧跟骨架：起步/爬速只小幅抖动；不同步主要交给熄灭灭点 + 错峰起裂
            _lavaDelayMul = Random.Range(0.7f, 1.35f);
            _lavaGrowMul = Random.Range(0.85f, 1.3f);
            _lavaLifeMul = Random.Range(0.7f, 1.3f);
            // 上限 1.05：存活时长由 StrengthSpec.Duration 定，抖太多会被回池截断
            _holdMul = Random.Range(0.7f, 1.05f);
            // 整体错峰：连裂开那一刻都不同时，全局才不会读成一次齐射
            _startDelay = Random.Range(0f, 0.22f);
            _burstPhase = Random.Range(0f, Mathf.PI * 2f);
            _flickerPhase = Random.Range(0f, Mathf.PI * 2f);
            _lavaFadeSeed = Random.Range(0f, 64f);
            if (_lavaLag != null)
                for (int i = 0; i < _lavaLag.Length; i++)
                    _lavaLag[i] = Random.Range(0f, 0.45f);
        }

        /// <summary>把线性进度揉成「一阵一顿」：裂缝是脆性扩展，走走停停，
        /// 匀速推进看着像刷子刷过去。振幅取到仍单调递增的上限内。</summary>
        float Burst(float t, float phase)
        {
            const float amp = 0.5f, k = 2.5f;
            float w = Mathf.PI * 2f * k;
            return Mathf.Clamp01(t + amp * Mathf.Sin(w * t + phase) / w);
        }

        void Awake() => Collect();

        void Collect()
        {
            if (_renderers != null) return;
            _renderers = GetComponentsInChildren<SpriteRenderer>(true);
            _baseColors = new Color[_renderers.Length];
            _glowScale = new float[_renderers.Length];
            _growLag = new float[_renderers.Length];
            _lavaLag = new float[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _baseColors[i] = _renderers[i].color;
                // 自愈：早期 G4 组装器把 Apply(0) 后的 alpha=0 烤进过 prefab，
                // 基色全透明会让裂地永远不可见——基色 alpha 视作满。
                if (_baseColors[i].a < 0.01f) _baseColors[i].a = 1f;
                // 缝底压暗免得与主缝叠成橙斑；毛刺压暗以保住主次（主缝才是"劈痕"）
                string n = _renderers[i].name;
                _glowScale[i] = n == "CrackCore" ? 0.35f
                              : n.StartsWith("Spur") ? 0.7f
                              : 1f;
                // 层与层之间错开推进：毛刺是被主缝撕出来的，得晚一步长
                _growLag[i] = n.StartsWith("Spur") ? 0.22f
                            : n == "CrackCore" ? 0.08f
                            : 0f;
            }
        }

        void OnEnable()
        {
            Collect(); // 池化复用时 Awake 只走过一次
            _elapsed = 0f;
            Roll(); // 池化复用：每次出场重摇，否则同一实例反复播同一段动画
            ApplyCardWidth();
            // 平躺基准必须是 Euler(90,0,·)：禁止读改 localEulerAngles（俯仰 90°
            // 万向节锁会把 x/y 搅坏，G4 组装期曾因此把错误朝向烤进 prefab）。
            if (RandomizeSpin)
                transform.localRotation = Quaternion.Euler(90f, 0f, Random.Range(0f, 360f));
            else
                transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Apply(0f);
        }

        void ApplyCardWidth()
        {
            float scale = Mathf.Max(0.01f, SizeScale);
            if (CardWidthFactor > 0f && BakedSize > 0.0001f)
            {
                float cardWidth = Units.StanceLayout.CardWidth;
                // 布局还没算过就只按强度档放大，保持烘制尺寸基准
                if (cardWidth > 0.0001f)
                    scale *= cardWidth * CardWidthFactor * AreaFactor / BakedSize;
            }
            transform.localScale = Vector3.one * scale;
        }

        void Update()
        {
            _elapsed += Time.deltaTime / Mathf.Max(0.01f, DurationScale);
            if (_elapsed < _startDelay)
            {
                Apply(0f); // 错峰等待：这一发还没开始裂
                return;
            }
            _elapsed -= _startDelay;
            _startDelay = 0f;
            if (_elapsed < FadeIn)
            {
                Apply(FadeIn <= 0f ? 1f : _elapsed / FadeIn);
                return;
            }
            float hold = Hold * _holdMul;
            float fadeOut = FadeOut * _holdMul;
            float afterHold = _elapsed - FadeIn - hold;
            if (afterHold <= 0f)
            {
                Apply(1f);
                return;
            }
            if (fadeOut <= 0f || afterHold >= fadeOut)
            {
                Apply(0f);
                return;
            }
            Apply(1f - afterHold / fadeOut);
        }

        void Apply(float t)
        {
            if (_renderers == null) return;
            float a = PeakAlpha * Mathf.Clamp01(t);
            float growth = Growth();
            float glowGrowth = GlowGrowth();
            float glow = Glow();
            float extinguish = LavaExtinguish();
            _block ??= new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                var c = _baseColors[i];
                _renderers[i].color = new Color(c.r, c.g, c.b, c.a * a);

                _renderers[i].GetPropertyBlock(_block);
                float lag = _growLag[i];
                _block.SetFloat(GrowthId, growth - lag);
                _block.SetFloat(GrowthModeId, GrowthMode);
                // 每个子面片的火再各自错开一点：主缝、缝底、每根毛刺不是同时着
                _block.SetFloat(GlowGrowthId, glowGrowth - lag - _lavaLag[i]);
                _block.SetFloat(LavaExtinguishId, extinguish);
                _block.SetFloat(LavaFadeSeedId, _lavaFadeSeed + i * 7.3f);
                // 缝底层与裂缝层同位重叠，两层等亮会在中心叠成一坨橙斑，故缝底压暗
                _block.SetFloat(GlowId, glow * _glowScale[i]);
                _block.SetFloat(FrontWidthId, FrontWidth);
                _block.SetFloat(EmberFloorId, EmberFloor);
                _block.SetFloat(MaskGainId, MaskGain);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        /// <summary>推进阈值。要越过 1 才能把 shader 里的抖动项（最多 +0.18）
        /// 也覆盖掉，否则最细的裂纹梢永远长不出来。</summary>
        float Growth()
        {
            // 1.47＝1.25（覆盖 shader 抖动项）+ 最大层间滞后 0.22，
            // 否则挂了滞后的毛刺层永远差一口气长不满
            const float full = 1.47f;
            float span = GrowTime * _growMul;
            if (span <= 0f) return full;
            float t = Burst(Mathf.Clamp01(_elapsed / span), _burstPhase);
            return Mathf.SmoothStep(0f, full, t);
        }

        float LavaStart => GrowTime * _growMul * Mathf.Max(0f, LavaDelay) * _lavaDelayMul;

        /// <summary>熔岩自己的推进阈值（喂 `_GlowGrowth`）：比裂缝晚 LavaDelay 起步、
        /// 慢 LavaGrowMul 倍爬完，于是火是「顺着已经裂开的缝爬进去」。</summary>
        float GlowGrowth()
        {
            // 熔岩要多推一截：层间滞后 0.22+0.28 与 shader 火口散布 ±0.2 都从这里扣
            const float full = 1.95f;
            float span = GrowTime * _growMul * Mathf.Max(0.05f, LavaGrowMul) * _lavaGrowMul;
            if (span <= 0f) return full;
            // 火爬进缝里同样走走停停，且与裂缝用不同相位，两条推进不会同步锁死
            float t = Burst(Mathf.Clamp01((_elapsed - LavaStart) / span),
                            _burstPhase + 2.1f);
            return Mathf.SmoothStep(0f, full, t);
        }

        /// <summary>熔岩强度：跟着骨架涨到峰值后**维持亮度**，空间上的熄灭交给
        /// <see cref="LavaExtinguish"/> —— 全局压暗会把几处火口齐灭，读作同步开关。</summary>
        float Glow()
        {
            if (GlowPeak <= 0f) return 0f;
            float t = _elapsed - LavaStart;
            if (t <= 0f) return 0f;
            float life = (FadeIn + (Hold + FadeOut) * _holdMul) *
                         Mathf.Clamp01(LavaLifeRatio * _lavaLifeMul) - LavaStart;
            if (life <= 0f || t >= life) return 0f;
            float rise = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(t / Mathf.Max(0.01f, GrowTime * _growMul * 0.55f)));
            float flicker = 1f - 0.18f * (0.5f + 0.5f * Mathf.Sin(_elapsed * 11.3f + _flickerPhase))
                                 * (0.6f + 0.4f * Mathf.Sin(_elapsed * 4.1f + _flickerPhase * 1.7f));
            return GlowPeak * rise * flicker;
        }

        /// <summary>熔岩空间熄灭进度（喂 `_LavaExtinguish`）：涨满后从几个随机灭点
        /// 渐变消失。进度曲线用平方，前半段只灭几处，后半段才连片冷掉。</summary>
        float LavaExtinguish()
        {
            if (GlowPeak <= 0f) return 1f;
            float t = _elapsed - LavaStart;
            if (t <= 0f) return 0f;
            float life = (FadeIn + (Hold + FadeOut) * _holdMul) *
                         Mathf.Clamp01(LavaLifeRatio * _lavaLifeMul) - LavaStart;
            if (life <= 0f) return 1f;
            // 前 ~35% 寿命只生长、不熄；之后才开始从灭点消退
            float fadeStart = life * 0.35f;
            if (t <= fadeStart) return 0f;
            float u = Mathf.Clamp01((t - fadeStart) / Mathf.Max(0.01f, life - fadeStart));
            return u * u;
        }
    }
}
