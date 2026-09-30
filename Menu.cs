using System;
using UnityEngine;

namespace BTScale
{
    // In-game settings window (default F11): render quality, DLSS preset, texture sharpness bias. Save writes BTScale.user.json.
    public class Menu : MonoBehaviour
    {
        static readonly string[] MipNames = { "Full", "2/3", "1/3", "Off" };
        KeyCode key = KeyCode.F11;
        bool show;
        Rect win = new Rect(40, 60, 420, 10);
        string note = "";

        void Awake()
        {
            // The menu is the way to reach every setting, so it always has a key: an unbound or unknown menuKey falls back to F11.
            var k = Main.ParseKey(Main.S.menuKey);
            if (k != KeyCode.None) key = k; else Main.S.menuKey = "F11";
        }

        float calibAt;

        void Update()
        {
            // Ctrl+<key> combinations belong to other mods and the debug shortcuts (FarSight uses Ctrl+F11).
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl && Input.GetKeyDown(key)) show = !show;
            if (calibAt > 0f && Time.realtimeSinceStartup >= calibAt) { calibAt = 0f; Calib.Start(); }
        }

        void OnGUI()
        {
            if (!show) return;
            win = GUILayout.Window(0x7B5C, win, Draw, "BTScale - DLSS   (" + Main.S.menuKey + " to close)");
        }

        // " (F8)" when a hotkey is bound, nothing otherwise.
        static string KeyHint(string name) { return Main.ParseKey(name) == KeyCode.None ? "" : "  (" + name + ")"; }

        static string ResText(int qi)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * Main.QualityRatio[qi]) & ~1);
            int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * Main.QualityRatio[qi]) & ~1);
            return w + "x" + h;
        }

        void Draw(int id)
        {
            var s = Main.S;

            Scaler.Enabled = GUILayout.Toggle(Scaler.Enabled, "Render-scale pipeline  -  off is the vanilla renderer" + KeyHint(s.toggleKey));
            bool dl = GUILayout.Toggle(Dlss.Enabled, "DLSS upscaling  -  off uses a plain stretch" + KeyHint(s.dlssKey));
            if (dl != Dlss.Enabled) { Dlss.Enabled = dl; s.dlss = dl; Dlss.Retry(); }

            GUILayout.Space(6);
            GUILayout.Label("Quality mode  -  renders at " + ResText(Main.QualityIndex) + ", outputs " + Screen.width + "x" + Screen.height);
            int qi = GUILayout.SelectionGrid(Main.QualityIndex, Main.QualityNames, 3);
            if (qi != Main.QualityIndex) { s.quality = Main.QualityNames[qi]; Main.ApplyToRuntime(); Dlss.Retry(); }

            GUILayout.Space(6);
            GUILayout.Label("DLSS preset  (Default lets NVIDIA choose per mode)");
            int pi = GUILayout.SelectionGrid(Main.PresetIndex, Main.PresetNames, 5);
            if (pi != Main.PresetIndex) { s.preset = Main.PresetNames[pi]; Main.ApplyToRuntime(); Dlss.Retry(); }

            GUILayout.Space(6);
            s.fullResOutlines = GUILayout.Toggle(s.fullResOutlines, "Full-resolution mech outlines, move cursor and mission boundary");
            Scaler.SkipUI = GUILayout.Toggle(Scaler.SkipUI, "Hide the game UI (for screenshots; close this menu to see the result)" + KeyHint(s.hideUiKey));

            GUILayout.Space(6);
            GUILayout.Label("Texture sharpness bias (compensates the lower render resolution)");
            int mi = GUILayout.SelectionGrid(MipBias.Level, MipNames, 4);
            if (mi != MipBias.Level) { s.mipLevel = mi; MipBias.SetLevel(mi); }

            if (s.debug)
            {
                GUILayout.Space(6);
                s.jitterTransparents = GUILayout.Toggle(s.jitterTransparents, "[debug] Jitter particles too (no visible gain seen; mech outlines wobble)");
                GUILayout.Label("[debug] Outline calibration: close this menu, keep the camera still with a mech outline visible");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start calibration in 3 s")) { calibAt = Time.realtimeSinceStartup + 3f; show = false; }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("[debug] Run benchmark over all modes (~1.5 min, keep the camera still)")) { show = false; Bench.Start(); }
            }

            GUILayout.Space(8);
            GUILayout.Label(Dlss.Describe());
            if (Dlss.LastFailure.Length > 0) GUILayout.Label("DLSS failed: " + Dlss.LastFailure + "  (change a setting to retry)");

            GUILayout.Space(4);
            GUILayout.Label("Uses NVIDIA DLSS. Unofficial mod, not sponsored or endorsed by NVIDIA.");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save")) { s.dlss = Dlss.Enabled; Main.Save(); note = "Saved to BTScale.user.json"; }
            if (GUILayout.Button("Close")) show = false;
            GUILayout.EndHorizontal();
            if (note.Length > 0) GUILayout.Label(note);

            GUI.DragWindow();
        }
    }
}
