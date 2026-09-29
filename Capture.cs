using System;
using System.Collections.Generic;
using System.IO;
using BattleTech.Rendering.UI;
using HBS;
using UnityEngine;

namespace BTScale
{
    // F7: writes PNGs of each pipeline stage on the next BTPostProcess call so they can be inspected offline.
    public static class Capture
    {
        public static bool Armed;
        static int n;

        public static void Describe(string label, RenderTexture rt)
        {
            Main.Log("  " + label + ": " + (rt == null ? "null" : rt.name + " " + rt.width + "x" + rt.height + " " + rt.format + " aa=" + rt.antiAliasing));
        }

        public static void Dump(string name, RenderTexture rt, bool flattenAlpha)
        {
            if (rt == null) return;
            try
            {
                int w = 1920, h = 1080;
                var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(rt, tmp);
                var prev = RenderTexture.active;
                RenderTexture.active = tmp;
                var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);
                var px = tex.GetPixels();
                var outPx = new Color32[px.Length];
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    float a = flattenAlpha ? Mathf.Clamp01(c.a) : 1f;
                    float r = c.r * a + 0.35f * (1f - a), g = c.g * a + 0.35f * (1f - a), b = c.b * a + 0.35f * (1f - a);
                    outPx[i] = new Color32(
                        (byte)(Mathf.Clamp01(Mathf.LinearToGammaSpace(r)) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.LinearToGammaSpace(g)) * 255f),
                        (byte)(Mathf.Clamp01(Mathf.LinearToGammaSpace(b)) * 255f), 255);
                }
                var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
                o.SetPixels32(outPx);
                File.WriteAllBytes(Path.Combine(Main.Dir, "cap" + n + "_" + name + ".png"), o.EncodeToPNG());
                UnityEngine.Object.Destroy(tex); UnityEngine.Object.Destroy(o);
                Main.Log("  wrote cap" + n + "_" + name + ".png from " + rt.name);
            }
            catch (Exception e) { Main.Log("  dump " + name + " failed: " + e.Message); }
        }

        // Motion vectors as DLSS receives them: signed x/y as red/green around mid-grey, and magnitude (8 px = white).
        public static void DumpMotion(RenderTexture mv, int renderW)
        {
            if (mv == null) return;
            try
            {
                int w = 1280, h = 720;
                var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                Graphics.Blit(mv, tmp);
                var prev = RenderTexture.active;
                RenderTexture.active = tmp;
                var tex = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);
                var px = tex.GetPixels();
                var signed = new Color32[px.Length];
                var mag = new Color32[px.Length];
                var dev = new Color32[px.Length];
                var mags = new float[px.Length];
                double sum = 0, max = 0; int nz = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float mx = px[i].r * renderW, my = px[i].g * renderW;          // roughly pixels (square pixels assumed for the picture)
                    signed[i] = new Color32(
                        (byte)(Mathf.Clamp01(0.5f + mx / 96f) * 255f), (byte)(Mathf.Clamp01(0.5f + my / 96f) * 255f), 128, 255);
                    float m = Mathf.Sqrt(mx * mx + my * my);
                    mags[i] = m;
                    byte g = (byte)(Mathf.Clamp01(m / 48f) * 255f);                // white = 48 px
                    mag[i] = new Color32(g, g, g, 255);
                    if (m > 0.05f) { nz++; sum += m; if (m > max) max = m; }
                }
                // Deviation from the local average (radius 24): objects whose motion differs from the terrain around them stand out.
                // Trees that carried no motion, or the wrong motion, would show up as bright shapes.
                var integral = new double[(w + 1) * (h + 1)];
                for (int y = 0; y < h; y++)
                {
                    double row = 0;
                    for (int x = 0; x < w; x++) { row += mags[y * w + x]; integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row; }
                }
                const int R = 24;
                double devSum = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int x0 = Mathf.Max(0, x - R), x1 = Mathf.Min(w, x + R + 1), y0 = Mathf.Max(0, y - R), y1 = Mathf.Min(h, y + R + 1);
                        double s = integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1] - integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0];
                        float d = Mathf.Abs(mags[y * w + x] - (float)(s / ((x1 - x0) * (y1 - y0))));
                        devSum += d;
                        byte g = (byte)(Mathf.Clamp01(d / 3f) * 255f);              // white = 3 px away from the neighbourhood
                        dev[y * w + x] = new Color32(g, g, g, 255);
                    }
                Main.Log("  MV mean deviation from neighbourhood: " + (devSum / px.Length).ToString("F3") + " px");
                Main.Log("  MV @DLSS input: " + mv.width + "x" + mv.height + " " + mv.format + " nonzero=" + (100.0 * nz / px.Length).ToString("F1") + "% meanPx=" + (nz > 0 ? sum / nz : 0).ToString("F2") + " maxPx=" + max.ToString("F1"));
                foreach (var pair in new[] { new KeyValuePair<string, Color32[]>("mv_signed", signed), new KeyValuePair<string, Color32[]>("mv_mag", mag), new KeyValuePair<string, Color32[]>("mv_dev", dev) })
                {
                    var o = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    o.SetPixels32(pair.Value);
                    File.WriteAllBytes(Path.Combine(Main.Dir, "cap" + (n + 1) + "_" + pair.Key + ".png"), o.EncodeToPNG());
                    UnityEngine.Object.Destroy(o);
                }
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { Main.Log("  DumpMotion failed: " + e.Message); }
        }

        public static void Begin(RenderTexture source, RenderTexture upscaled)
        {
            n++;
            Main.Log("CAPTURE " + n + " Screen=" + Screen.width + "x" + Screen.height + " camPixel=" + Camera.main.pixelWidth + "x" + Camera.main.pixelHeight);
            Describe("BTPostProcess source (low-res world)", source);
            Describe("upscaled source", upscaled);
            Describe("Main UI RT", UICameraRenderer.MainUIRT);
            var uiCam = UICameraRenderer.Instance != null ? UICameraRenderer.Instance.UICamera : null;
            if (uiCam != null) Main.Log("  UICam pixel=" + uiCam.pixelWidth + "x" + uiCam.pixelHeight + " ortho=" + uiCam.orthographicSize + " aspect=" + uiCam.aspect + " rect=" + uiCam.rect);
            var wc = UICameraRenderer.Instance != null ? UICameraRenderer.Instance.InWorldCamera : null;
            if (wc != null) Main.Log("  inWorldCam pixel=" + wc.pixelWidth + "x" + wc.pixelHeight + " ortho=" + wc.orthographicSize + " aspect=" + wc.aspect);
            Main.Log("  _ScreenSize=" + Shader.GetGlobalVector("_ScreenSize") + " _BTScreenWidth=" + Shader.GetGlobalFloat("_BTScreenWidth"));
            var em = LazySingletonBehavior<ElementManager>.Instance;
            var gui = em != null ? em.gameUIRT : null;
            Describe("ElementManager.gameUIRT (_BT_ElementUI)", gui);
            var blur = Shader.GetGlobalTexture("_UIBlurTex") as RenderTexture;
            Describe("_UIBlurTex", blur);
            Describe("global _BT_MainUI", Shader.GetGlobalTexture("_BT_MainUI") as RenderTexture);
            Dump("1_world_lowres", source, false);
            Dump("2_ui", UICameraRenderer.MainUIRT, true);
            Dump("4_element_ui", gui, true);
            Dump("5_ui_blur", blur, true);
        }

        public static void End()
        {
            Dump("3_final", Scaler.FinalRT, false);
            Armed = false;
        }
    }
}
