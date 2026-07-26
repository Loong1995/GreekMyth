using ClientBattle.VFX;
using UnityEngine;

namespace ClientBattle.Units
{
    // =========================================================================
    // 【第4层 演出对象】近 3D 舞台底图：地面水平板 ⊥ 天空竖直板拼接。
    // 只负责两块底图的构建与每帧取尺；站位/落点归 ArenaSlotLayout。
    // 资源协议：Resources/ClientBattle/Arena/arena_<stage>.png（正俯视 16:9 全宽）
    //           Resources/ClientBattle/Arena/sky_<stage>.png（横构图天穹）
    // 出图规范：docs/dev/near3d_evaluation.md §七；表现总览 docs/client/arena_stage.md。
    // =========================================================================

    public class ArenaStageView : MonoBehaviour
    {
        /// <summary>当前舞台 id（后续接舞台机制后由战报元数据驱动）。</summary>
        public static string StageId = "olympus";

        // ---- 几何常量（世界单位；地面高度同源 CameraFitter.PilotGroundY）----
        public const float GroundY = CameraFitter.PilotGroundY;
        // 地面板矩形 = BattlefieldLayout「正好拍全」反算（近缘卡屏底、侧边卡
        // 接缝处屏边，仅 EdgeGuard 微量外扩防走样）；站位分区与本板同源。
        static float GroundFarZ => BattlefieldLayout.GroundFarSeamZ; // 地天接缝
        const float SkyMargin = 1.2f;          // 天空高度冗余

        // 地面＝不透明写深度的 Quad 网格（不是 Sprite）：厂包裂地贴花全是屏幕空间
        // 深度投影（RFX1_UberDecal / RFX4/Decal 采样 _CameraDepthTexture），
        // 透明 Sprite 不写深度 → 贴花没有可重建表面必然全空（P-32）。
        // 贴图仍是同一张 arena_<stage>；URP/Unlit 同样不参与光照，故亮度与改造前一致。
        MeshRenderer _ground;
        SpriteRenderer _sky;

        /// <summary>尝试构建近 3D 舞台；资源缺失或非透视模式返回 false。</summary>
        public static bool TryBuild(Transform parent, out ArenaStageView view)
        {
            view = null;
            if (!CameraFitter.PerspectivePilot) return false;

            var ground = Placeholder.PlaceholderFactory.TryLoadSprite("Arena", "arena_" + StageId);
            var sky = Placeholder.PlaceholderFactory.TryLoadSprite("Arena", "sky_" + StageId);
            if (ground == null || sky == null) return false;

            var go = new GameObject("ArenaStage");
            go.transform.SetParent(parent, false);
            view = go.AddComponent<ArenaStageView>();
            view.BuildQuads(ground, sky);
            return true;
        }

        void BuildQuads(Sprite ground, Sprite sky)
        {
            // 地面：绕 X 转 90° 平躺（贴图顶边 → 远端 +z，与出图「上远下近」一致）
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "ArenaGround";
            Destroy(g.GetComponent<Collider>()); // 只作底图，不参与任何碰撞
            g.transform.SetParent(transform, false);
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.position = new Vector3(0f, GroundY, BattlefieldLayout.GroundCenterZ);
            _ground = g.GetComponent<MeshRenderer>();
            _ground.sharedMaterial = BuildGroundMaterial(ground);
            _ground.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ground.receiveShadows = false;

            // 天空：竖直板立在地面远端，底边与地面接缝
            var s = new GameObject("ArenaSky");
            s.transform.SetParent(transform, false);
            s.transform.position = new Vector3(0f, GroundY, GroundFarZ);
            _sky = s.AddComponent<SpriteRenderer>();
            _sky.sprite = sky;
            _sky.sortingOrder = -110;
        }

        /// <summary>不透明写深度的无光照材质；UV 取 sprite 在图集里的实际矩形
        /// （Single 全图时即整张）。Cull Off 免去 Quad 正反面朝向的猜测。</summary>
        static Material BuildGroundMaterial(Sprite ground)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            var mat = new Material(shader) { name = "ArenaGroundMat" };
            var tex = ground.texture;
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            var rect = ground.textureRect;
            var scale = new Vector2(rect.width / tex.width, rect.height / tex.height);
            var offset = new Vector2(rect.x / tex.width, rect.y / tex.height);
            mat.mainTextureScale = scale;
            mat.mainTextureOffset = offset;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
            }
            // Opaque + 写深度：贴花可投的唯一前提，勿改成 Transparent
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return mat;
        }

        void LateUpdate() => FitToCamera();

        /// <summary>每帧按相机视野重算尺寸（机型/分辨率热切换安全）。</summary>
        void FitToCamera()
        {
            var cam = Camera.main;
            if (cam == null || _ground == null || _sky == null) return;

            // ---- 地面：「正好拍全」矩形（BattlefieldLayout 反算）----
            // 近缘 = 屏底视线落地（下侧恰好卡屏幕下沿）；宽 = 接缝处屏边半宽
            // （左右两侧在接缝处恰好卡屏幕边缘，近处板略宽被裁掉，全程不露黑）。
            // 站位分区与本板同一来源，所以站位天然全部入画。
            // Quad 网格本身是 1×1 世界单位，localScale 即最终世界尺寸。
            BattlefieldLayout.RecalcFromCamera(cam);
            float guard = BattlefieldLayout.EdgeGuard;
            float width = (BattlefieldLayout.GroundHalfWidth + guard) * 2f;
            float nearZ = BattlefieldLayout.GroundNearZ - guard;
            float length = GroundFarZ - nearZ;
            _ground.transform.localScale = new Vector3(width, length, 1f);
            _ground.transform.position = new Vector3(0f, GroundY, (nearZ + GroundFarZ) * 0.5f);

            // ---- 天空：只按「接缝→屏顶」实际需要的高度取尺，蓝天保证入画 ----
            // 屏顶射线在天空板 z 处的世界高度：yTop = camY + dz·tan(−pitch + fov/2)
            float dz = GroundFarZ - cam.transform.position.z;
            float topElevDeg = -cam.transform.eulerAngles.x + cam.fieldOfView * 0.5f;
            float yTop = cam.transform.position.y + dz * Mathf.Tan(topElevDeg * Mathf.Deg2Rad);
            float neededH = Mathf.Max(1f, yTop - GroundY) * SkyMargin;
            // 宽度须按斜向路径：45° 俯视下到竖板的实际光路 ≈ dz/cos(pitch)，
            // 比 VisibleHalfWidthAt 的纵深轴距离宽 ~1.4 倍；再留冗余 → ×2
            float neededW = CameraFitter.VisibleHalfWidthAt(cam, GroundFarZ) * 2f * 2f;
            var sSize = _sky.sprite.bounds.size;
            // 宽 cover、高允许适度拉伸（天空可拉，不许露黑边）
            float sx = neededW / sSize.x;
            float sy = Mathf.Max(neededH / sSize.y, sx * 0.5f);
            _sky.transform.localScale = new Vector3(Mathf.Max(sx, sy * 0.3f), sy, 1f);
            // 底边贴地面远缘
            float shownHalfH = sSize.y * sy * 0.5f;
            _sky.transform.position = new Vector3(0f, GroundY + shownHalfH, GroundFarZ);
        }
    }
}
