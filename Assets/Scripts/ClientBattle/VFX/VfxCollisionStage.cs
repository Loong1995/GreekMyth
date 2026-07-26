using ClientBattle.Units;
using UnityEngine;

namespace ClientBattle.VFX
{
    // =========================================================================
    // 【第5层】特效专用碰撞层：给厂包特效提供「可撞的地面 + 可撞的卡牌」。
    //
    // 为什么必须有：厂包主件（RFX1/RFX4 的 EffectN）不是"摆在一点上播"的散件，
    // 而是一整套出手流程 —— 位移脚本把弹体推出去，**撞到碰撞体**才生成它的
    // 命中件（EffectsOnCollision）；碎石/水花类的粒子也靠 Collision 模块落地。
    // 我们的舞台底图是特意去掉碰撞体的（ArenaStageView），全场一个碰撞体都没有，
    // 于是这类件永远只演前半段，被误判成"标准化不出可用组件"（见 P-40）。
    //
    // 为什么单独开一层：这些碰撞体只给特效的 raycast / 粒子碰撞看，不参与任何
    // 战斗判定（战斗全在服务器）、也不该被点击拾取吃到。层名固定 VfxCollision，
    // 缺层时退回 Default 并告警，绝不静默改行为。
    //
    // 红线：碰撞体是**表现层附属物**，禁止任何逻辑读它做判定。
    // =========================================================================

    public static class VfxCollisionStage
    {
        public const string LayerName = "VfxCollision";

        const string GroundName = "VfxGroundCollider";
        const string CardName = "VfxHitBox";

        /// <summary>地面碰撞板尺寸（世界单位）。取得比舞台底图大，免得弹道打在图外
        /// 就穿空 —— 它不可见，做大无副作用。</summary>
        const float GroundSpan = 200f;
        const float GroundThickness = 0.5f;

        /// <summary>卡牌碰撞盒厚度：卡是平板，给一点厚度才不会被斜向弹道擦过。</summary>
        const float CardThickness = 0.3f;

        static bool _layerWarned;

        /// <summary>特效碰撞层号；工程未建该层时退回 Default 并告警一次。</summary>
        public static int Layer
        {
            get
            {
                int layer = LayerMask.NameToLayer(LayerName);
                if (layer >= 0) return layer;
                if (!_layerWarned)
                {
                    _layerWarned = true;
                    Debug.LogWarning($"[VfxCollisionStage] 缺少 Layer「{LayerName}」，" +
                                     "特效碰撞体退回 Default 层（表现正常，但无法与其他射线隔离）。");
                }
                return 0;
            }
        }

        /// <summary>建整盘的特效碰撞体：地面一块 + 每张卡一块。建场末尾调一次即可。</summary>
        public static void Ensure(BattleBoardView board)
        {
            if (board == null) return;
            EnsureGround(board.transform);
            foreach (var unit in board.AllUnits) AttachTo(unit);
        }

        /// <summary>地面碰撞板：顶面与舞台地面同高（GroundY），向下给厚度。</summary>
        public static void EnsureGround(Transform parent)
        {
            var existing = parent != null ? parent.Find(GroundName) : null;
            if (existing != null) return;

            var go = new GameObject(GroundName) { layer = Layer };
            go.transform.SetParent(parent, false);
            // 顶面贴齐地面高度：盒心下沉半个厚度
            go.transform.position = new Vector3(0f, ArenaStageView.GroundY - GroundThickness * 0.5f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(GroundSpan, GroundThickness, GroundSpan);
        }

        /// <summary>卡牌碰撞盒：随卡牌一起倾斜（挂在卡节点下），尺寸取运行期卡面。
        /// 弹道撞在卡面上 → 命中件生成在卡身处，和"打到人"的读法一致。</summary>
        public static void AttachTo(UnitView unit)
        {
            if (unit == null) return;
            if (unit.transform.Find(CardName) != null) return;

            var go = new GameObject(CardName) { layer = Layer };
            go.transform.SetParent(unit.transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(
                Mathf.Max(0.1f, StanceLayout.CardWidth),
                Mathf.Max(0.1f, StanceLayout.CardHeight),
                CardThickness);
        }
    }
}
