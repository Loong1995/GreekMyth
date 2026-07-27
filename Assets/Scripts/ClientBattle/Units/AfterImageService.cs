using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第5层 基础设施】突进残影：沿突进路径留下逐渐淡出的卡牌残像。
    //
    // 这是 2D 动作游戏表达「快」的标准手法：位移本身只说明「换了位置」，
    // 拖在后面的一串残像才说明「冲过去了」。
    //
    // 不走 VFXManager 池：残影是**当前卡面的运行期快照**（立绘/卡框随石化、
    // 压暗、阵营染色实时变化），不是可预制的 prefab 特效。故本类自带环形池，
    // 复用固定数量的影子对象，突进期间零 Instantiate。
    //
    // sorting −2：在卡牌（0/1）与势能卡后光环（−1）之下、接地阴影（−3）之上。
    // 文档：docs/client/rendering_layout.md §四 / performance_mechanisms.md
    // =========================================================================

    public static class AfterImageService
    {
        /// <summary>环形池容量。一次突进约 4~6 张，多单位连续突进也够轮转。</summary>
        const int PoolSize = 24;
        const int GhostOrder = -2;

        static Transform _pool;
        static Ghost[] _ghosts;
        static int _cursor;

        /// <summary>拍一张当前卡面的残像。life＝淡出时长（已按倍速换算的秒）。</summary>
        public static void Emit(UnitView unit, float life)
        {
            if (unit == null || unit.Defeated) return;
            if (!EnsurePool()) return;
            _ghosts[_cursor].Show(unit, Mathf.Max(0.05f, life),
                StagePerformanceConfig.GhostAlpha, StagePerformanceConfig.GhostShrink);
            _cursor = (_cursor + 1) % _ghosts.Length;
        }

        /// <summary>硬停止/跳过：立刻收掉全部在场残影。</summary>
        public static void ClearAll()
        {
            if (_ghosts == null) return;
            foreach (var ghost in _ghosts)
                if (ghost != null) ghost.Hide();
        }

        static bool EnsurePool()
        {
            // 不能用 ?.：场景重建后旧引用是「已销毁的假 null」（同 CameraShaker 教训）
            if (_pool != null && _ghosts != null) return true;
            var root = new GameObject("~AfterImagePool");
            _pool = root.transform;
            _ghosts = new Ghost[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("ghost");
                go.transform.SetParent(_pool, false);
                _ghosts[i] = go.AddComponent<Ghost>();
                _ghosts[i].Init();
            }
            _cursor = 0;
            return true;
        }

        sealed class Ghost : MonoBehaviour
        {
            SpriteRenderer _frame, _portrait;
            Color _frameFrom, _portraitFrom;
            Vector3 _scaleFrom;
            float _life, _elapsed;

            public void Init()
            {
                _frame = NewLayer("GhostFrame");
                _portrait = NewLayer("GhostPortrait");
                gameObject.SetActive(false);
            }

            SpriteRenderer NewLayer(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = GhostOrder;
                return sr;
            }

            public void Show(UnitView unit, float life, float alpha, float shrink)
            {
                var src = unit.transform;
                transform.SetPositionAndRotation(src.position, src.rotation);
                _scaleFrom = src.localScale * (1f - shrink);
                transform.localScale = _scaleFrom;

                _frameFrom = Copy(_frame, unit.FrameRenderer, alpha);
                _portraitFrom = Copy(_portrait, unit.PortraitRenderer, alpha);

                _life = life;
                _elapsed = 0f;
                gameObject.SetActive(true);
            }

            /// <summary>拷贝一层的 sprite + 局部姿态 + 当前颜色（含压暗/石化/染色）。</summary>
            static Color Copy(SpriteRenderer dst, SpriteRenderer src, float alpha)
            {
                if (src == null)
                {
                    dst.sprite = null;
                    return Color.clear;
                }
                dst.sprite = src.sprite;
                dst.transform.localPosition = src.transform.localPosition;
                dst.transform.localRotation = src.transform.localRotation;
                dst.transform.localScale = src.transform.localScale;
                var c = src.color;
                c.a *= alpha;
                dst.color = c;
                return c;
            }

            public void Hide()
            {
                _life = 0f;
                gameObject.SetActive(false);
            }

            void Update()
            {
                if (_life <= 0f) return;
                _elapsed += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(_elapsed / _life);
                if (k <= 0f)
                {
                    Hide();
                    return;
                }
                Fade(_frame, _frameFrom, k);
                Fade(_portrait, _portraitFrom, k);
                // 继续收缩：尾端比头端更小，一串下来是收敛的锥形而非等宽复读
                transform.localScale = _scaleFrom * Mathf.Lerp(0.94f, 1f, k);
            }

            static void Fade(SpriteRenderer sr, Color from, float k)
            {
                if (sr == null || sr.sprite == null) return;
                sr.color = new Color(from.r, from.g, from.b, from.a * k);
            }
        }
    }
}
