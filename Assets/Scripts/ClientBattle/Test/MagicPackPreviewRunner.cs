using System.Collections.Generic;
using ClientBattle.VFX;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClientBattle.Test
{
    // =========================================================================
    // Magic Effects Pack 1 可靠预览：透视 + HDR/Bloom + Effect1–33 循环。
    // 入口：菜单 GreekMyth → Magic Pack → 可靠预览（一键）
    // =========================================================================

    public sealed class MagicPackPreviewRunner : MonoBehaviour
    {
        const float RespawnSeconds = 3.2f;
        const float EffectScale = 0.85f;

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
                _status = "无 Prefab：请用菜单 GreekMyth/Magic Pack/可靠预览（一键）启动";
                return;
            }
            SpawnCurrent();
        }

        void Update()
        {
            if (Prefabs == null || Prefabs.Count == 0) return;

            // 只用 Input System 一条通道；勿再叠 OnGUI，否则同帧 Step 两次跳两个。
            // 须先点 Game 窗口，否则 Keyboard.current 收不到键。
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)
                    Step(-1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)
                    Step(1);
                if (kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    SpawnCurrent();
                if (kb.digit1Key.wasPressedThisFrame) JumpToNameContains("Effect17");
                if (kb.digit2Key.wasPressedThisFrame) JumpToNameContains("Effect18");
                if (kb.digit3Key.wasPressedThisFrame) JumpToNameContains("Effect19");
            }

            if (Time.unscaledTime >= _respawnAt)
                SpawnCurrent();
        }

        void JumpToNameContains(string token)
        {
            for (int i = 0; i < Prefabs.Count; i++)
            {
                if (Prefabs[i] != null && Prefabs[i].name.IndexOf(token) >= 0)
                {
                    _index = i;
                    SpawnCurrent();
                    return;
                }
            }
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
            string tag = HintFor(prefab.name);
            _status = $"[{_index + 1}/{Prefabs.Count}] {prefab.name}{tag}  |  先点Game窗  ←→切换  R重播  1/2/3=反制/战神环/雷命中";
        }

        static string HintFor(string name)
        {
            if (name.Contains("Effect18")) return "  【战神之勇 aura_ares_might】";
            if (name.Contains("Effect19")) return "  【宙斯命中 hit_lightning】";
            if (name.Contains("Effect17")) return "  【雅典娜反制 hit_shield_counter】";
            return "";
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
            GUI.Box(new Rect(pad, pad, 760, 64), "");
            GUI.Label(new Rect(pad + 10, pad + 8, 740, 48),
                string.IsNullOrEmpty(_status) ? "Magic Pack 1 Preview（先点一下 Game 窗口再按键）" : _status);
        }
    }
}
