using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClientBattle.Test
{
    // =========================================================================
    // RFX4 可靠预览：透视相机 + HDR/Bloom + 循环重生。勿接到战斗演出。
    // 入口：菜单 GreekMyth → RFX4 可靠预览（一键）
    // =========================================================================

    public sealed class Rfx4PreviewRunner : MonoBehaviour
    {
        const float RespawnSeconds = 2.8f;
        const float EffectScale = 1f;

        [SerializeField] List<GameObject> Prefabs = new List<GameObject>();
        [SerializeField] float AutoRespawnSeconds = RespawnSeconds;

        int _index;
        float _respawnAt;
        GameObject _current;
        string _status = "";

        public void SetPrefabs(List<GameObject> prefabs)
        {
            Prefabs = prefabs ?? new List<GameObject>();
        }

        void Start()
        {
            ConfigureCamera();
            BattlePostFx.Ensure();
            if (Prefabs == null || Prefabs.Count == 0)
            {
                _status = "无 Prefab：请用菜单 GreekMyth/RFX4 可靠预览（一键）启动";
                return;
            }
            SpawnCurrent();
        }

        void Update()
        {
            if (Prefabs == null || Prefabs.Count == 0) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)
                    Step(-1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
                    Step(1);
                if (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    SpawnCurrent();
            }

            if (Time.unscaledTime >= _respawnAt)
                SpawnCurrent();
        }

        void Step(int delta)
        {
            _index = (_index + delta + Prefabs.Count) % Prefabs.Count;
            SpawnCurrent();
        }

        void SpawnCurrent()
        {
            if (_current != null) Destroy(_current);
            var prefab = Prefabs[_index];
            if (prefab == null)
            {
                _status = $"[{_index + 1}/{Prefabs.Count}] 槽位空";
                _respawnAt = Time.unscaledTime + AutoRespawnSeconds;
                return;
            }

            _current = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _current.name = prefab.name + "(Preview)";
            _current.transform.localScale = Vector3.one * EffectScale;
            _respawnAt = Time.unscaledTime + AutoRespawnSeconds;
            _status = $"[{_index + 1}/{Prefabs.Count}] {prefab.name}  |  ← → 切换  R/空格 重播";
        }

        static void ConfigureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
                go.tag = "MainCamera";
            }

            cam.orthographic = false;
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.045f, 0.06f, 1f);
            cam.transform.position = new Vector3(0f, 2.2f, -7f);
            cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
        }

        void OnGUI()
        {
            const int pad = 12;
            GUI.color = Color.white;
            GUI.Box(new Rect(pad, pad, 640, 64), "");
            GUI.Label(new Rect(pad + 10, pad + 8, 620, 48),
                string.IsNullOrEmpty(_status)
                    ? "RFX4 Preview"
                    : _status);
        }
    }
}
