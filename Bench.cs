using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BTScale
{
    // Debug tool: steps through native rendering and every quality mode on a still camera and logs average FPS, 1 % low,
    // frame time and VRAM for each, so the cost of each mode is measured in one run instead of read off an overlay.
    public class Bench : MonoBehaviour
    {
        struct Step { public string name; public bool pipeline, dlss; public string quality; }

        static readonly Step[] Steps =
        {
            new Step { name = "Native (pipeline off)", pipeline = false, dlss = false },
            new Step { name = "DLAA", pipeline = true, dlss = true, quality = "DLAA" },
            new Step { name = "Ultra Quality", pipeline = true, dlss = true, quality = "Ultra Quality" },
            new Step { name = "Quality", pipeline = true, dlss = true, quality = "Quality" },
            new Step { name = "Balanced", pipeline = true, dlss = true, quality = "Balanced" },
            new Step { name = "Performance", pipeline = true, dlss = true, quality = "Performance" },
            new Step { name = "Ultra Performance", pipeline = true, dlss = true, quality = "Ultra Performance" },
            new Step { name = "Quality, DLSS off (bilinear)", pipeline = true, dlss = false, quality = "Quality" },
        };

        const float Settle = 3f, Sample = 8f, ReadyTimeout = 15f;
        static Bench instance;
        public static bool Running;
        static string status = "";

        void Awake() { instance = this; }

        public static void Start()
        {
            if (Running || instance == null) return;
            if (!Scaler.InCombat) { Main.Log("BENCH: not in a mission, nothing to measure"); return; }
            instance.StartCoroutine(instance.Run());
        }

        void OnGUI()
        {
            if (Running) GUI.Label(new Rect(10, 30, 1000, 24), status);
        }

        static bool Pipeline(bool want)
        {
            var cam = Scaler.MainCam;
            bool on = Scaler.Active(cam);
            return on == want;
        }

        IEnumerator Run()
        {
            Running = true;
            var s = Main.S;
            string q0 = s.quality; bool pipeline0 = Scaler.Enabled, dlss0 = Dlss.Enabled, dlssSetting0 = s.dlss;
            var report = new StringBuilder();
            Main.Log("BENCH start: " + SystemInfo.graphicsDeviceName + ", " + Screen.width + "x" + Screen.height + ", keep the camera still");

            for (int i = 0; i < Steps.Length; i++)
            {
                var st = Steps[i];
                Scaler.Enabled = st.pipeline;
                Dlss.Enabled = st.dlss; s.dlss = st.dlss;
                if (st.quality != null) { s.quality = st.quality; Main.ApplyToRuntime(); }
                Dlss.Retry();

                // Wait until the requested mode is really in effect (target recreated, DLSS ready), then let it settle.
                string label = "Bench " + (i + 1) + "/" + Steps.Length + ": " + st.name;
                float t0 = Time.unscaledTime;
                bool ready = false;
                while (Time.unscaledTime - t0 < ReadyTimeout)
                {
                    status = label + " (switching)";
                    bool right = Pipeline(st.pipeline);
                    if (right && st.pipeline)
                    {
                        var lo = Scaler.LowRT;
                        int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * Main.Ratio) & ~1);
                        right = lo != null && lo.width == w && (!st.dlss || Dlss.Ready);
                    }
                    if (right) { ready = true; break; }
                    yield return null;
                }
                if (!ready) Main.Log("BENCH: " + st.name + " did not become ready in " + ReadyTimeout + " s, measuring anyway");

                t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < Settle) { status = label + " (settling)"; yield return null; }

                var dts = new List<float>(1024);
                t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < Sample)
                {
                    status = label + " (measuring " + (Sample - (Time.unscaledTime - t0)).ToString("F0") + " s)";
                    dts.Add(Time.unscaledDeltaTime);
                    yield return null;
                }

                double sum = 0; foreach (var d in dts) sum += d;
                var sorted = dts.ToArray(); Array.Sort(sorted);
                float avgFps = (float)(dts.Count / sum);
                float p50 = sorted[sorted.Length / 2] * 1000f;
                float p99 = sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * 0.99f))] * 1000f;
                ulong use, budget; bool haveVram = Dlss.VramMB(out use, out budget);
                string line = string.Format("{0,-30} avg {1,6:F1} fps   median {2,6:F2} ms   1% low {3,6:F1} fps ({4,6:F2} ms)   VRAM {5}",
                    st.name, avgFps, p50, 1000f / p99, p99, haveVram ? use + " / " + budget + " MB" : "n/a");
                report.AppendLine(line);
                Main.Log("BENCH " + line);
            }

            Scaler.Enabled = pipeline0; Dlss.Enabled = dlss0; s.dlss = dlssSetting0; s.quality = q0;
            Main.ApplyToRuntime(); Dlss.Retry();
            Main.Log("BENCH summary (" + SystemInfo.graphicsDeviceName + ", " + Screen.width + "x" + Screen.height + "):\n" + report);
            status = "";
            Running = false;
        }
    }
}
