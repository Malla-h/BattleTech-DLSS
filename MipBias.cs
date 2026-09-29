using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BTScale
{
    // Rendering below output resolution samples textures at coarser mips than a native render would. Upscalers expect the game to
    // compensate with a negative texture LOD bias of log2(render/output) - 1 (about -1.585 for 2/3 scale).
    public class MipBias : MonoBehaviour
    {
        // Fraction of the recommended bias to apply: full, two thirds, a third, none. Cycled live with Ctrl+F6 while tuning by eye.
        static readonly float[] Levels = { 1f, 0.63f, 0.32f, 0f };
        static int level;
        public static int Level { get { return level; } }
        public static void SetLevel(int i) { level = Mathf.Clamp(i, 0, Levels.Length - 1); Main.Log("Mip bias level " + Levels[level]); }
        public static void Cycle() { SetLevel((level + 1) % Levels.Length); Main.S.mipLevel = level; }
        public static float Current;                       // bias currently applied to textures (0 = none)
        static readonly HashSet<int> done = new HashSet<int>();
        Coroutine scan;

        static float Target()
        {
            var lo = Scaler.LowRT;
            if (lo == null || !Dlss.Ready || Levels[level] <= 0f) return 0f;
            return (Mathf.Log(lo.width / (float)Screen.width, 2f) - 1f) * Levels[level];
        }

        void Update()
        {
            float t = Target();
            if (Mathf.Abs(t - Current) > 0.001f)
            {
                Current = t;
                done.Clear();                              // re-apply everything at the new value (also restores 0 when switched off)
                if (scan != null) StopCoroutine(scan);
                scan = StartCoroutine(Scan());
                Main.Log("Texture mip bias target " + t.ToString("F3"));
            }
            else if (scan == null && Current != 0f && Time.frameCount % 600 == 0)
                scan = StartCoroutine(Scan());             // pick up textures loaded since the last pass
        }

        IEnumerator Scan()
        {
            float bias = Current;
            var all = Resources.FindObjectsOfTypeAll<Texture>();
            int changed = 0, n = 0;
            foreach (var tex in all)
            {
                if (tex == null) continue;
                if (++n % 250 == 0) yield return null;    // spread the work over frames
                if (!done.Add(tex.GetInstanceID())) continue;
                if (!(tex is Texture2D) && !(tex is Texture2DArray)) continue;
                if (tex.mipMapBias == bias) continue;
                tex.mipMapBias = bias;
                changed++;
            }
            if (changed > 0) Main.Log("Mip bias " + bias.ToString("F3") + " applied to " + changed + " of " + all.Length + " textures");
            scan = null;
        }
    }

    // Transparents and VFX are otherwise drawn with the unjittered matrix, which DLSS is not told about.
    [HarmonyLib.HarmonyPatch(typeof(TaaComponent), "SetProjectionMatrix")]
    static class Taa_SetProjectionMatrix
    {
        static void Postfix()
        {
            var cam = Scaler.MainCam;
            if (cam != null && Scaler.Active(cam) && Dlss.Ready) cam.useJitteredProjectionMatrixForTransparentRendering = true;
        }
    }
}
