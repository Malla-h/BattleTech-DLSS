using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTScale
{
    // Ctrl+F10 with a still camera and a visible mech outline: measures how the outline texture (gameUIRT) moves when the jitter
    // changes, so the compensation can be derived from data instead of guessed. Results go to BTScale.log.
    public static class Calib
    {
        public static int Left;
        static readonly List<double[]> rows = new List<double[]>();   // jx, jy, cx, cy

        public static void Start()
        {
            rows.Clear();
            sheet.Clear();
            Left = 32;
            Main.Log("CAL start (fullResOutlines=" + Main.S.fullResOutlines + ", jitterTransparents=" + Main.S.jitterTransparents + ", outlineSign=" + Dlss.OutlineSign + ")");
        }

        public static void Sample(RenderTexture elem, bool fromDlss)
        {
            if (Left <= 0) return;
            Left--;
            try
            {
                const int W = 640, H = 360;
                var tmp = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(elem, tmp);
                var prev = RenderTexture.active;
                RenderTexture.active = tmp;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);
                var px = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                double sa = 0, sx = 0, sy = 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        double a = px[y * W + x].a;
                        if (a < 8) continue;
                        sa += a; sx += a * (x + 0.5); sy += a * (y + 0.5);
                    }
                if (sa <= 0) { Main.Log("CAL frame: no outline pixels visible"); return; }
                double scale = elem.width / (double)W;                     // convert to pixels of the low-res render
                double cx = sx / sa * scale, cy = sy / sa * scale;
                var j = Dlss.JitterPx;
                rows.Add(new[] { (double)j.x, (double)j.y, cx, cy });
                if (sheet.Count < 6) SheetFrame(elem, cx, cy);
                Main.Log("CAL f=" + Left + " jit=(" + j.x.ToString("F3") + "," + j.y.ToString("F3") + ") centroid=(" + cx.ToString("F3") + "," + cy.ToString("F3") + ") mass=" + sa.ToString("F0") + " dlss=" + fromDlss);
                if (Left == 0) Report();
            }
            catch (Exception e) { Main.Log("CAL failed: " + e.Message); Left = 0; }
        }

        // Six consecutive full-resolution crops of the outline texture in a fixed window, for a visual look at what changes.
        static readonly List<Color32[]> sheet = new List<Color32[]>();
        const int C = 300;
        static int ox, oy;

        static void SheetFrame(RenderTexture elem, double cx, double cy)
        {
            if (sheet.Count == 0) { ox = Mathf.Clamp((int)cx - C / 2, 0, elem.width - C); oy = Mathf.Clamp((int)cy - C / 2, 0, elem.height - C); }
            var full = RenderTexture.GetTemporary(elem.width, elem.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(elem, full);
            var prev = RenderTexture.active;
            RenderTexture.active = full;
            var tex = new Texture2D(C, C, TextureFormat.RGBA32, false, true);
            tex.ReadPixels(new Rect(ox, oy, C, C), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(full);
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            for (int i = 0; i < px.Length; i++)
            {
                float a = px[i].a / 255f;
                byte r = (byte)Mathf.Clamp(px[i].r * a + 90f * (1f - a), 0f, 255f), g = (byte)Mathf.Clamp(px[i].g * a + 90f * (1f - a), 0f, 255f), b = (byte)Mathf.Clamp(px[i].b * a + 90f * (1f - a), 0f, 255f);
                px[i] = new Color32(r, g, b, 255);
            }
            sheet.Add(px);
            if (sheet.Count == 6)
            {
                var o = new Texture2D(C * 3 + 20, C * 2 + 10, TextureFormat.RGBA32, false);
                var fill = new Color32[o.width * o.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
                o.SetPixels32(fill);
                for (int k = 0; k < 6; k++) o.SetPixels32((k % 3) * (C + 10), (k / 3) * (C + 10), C, C, sheet[k]);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(Main.Dir, "calib_sheet_" + (Main.S.fullResOutlines ? "fullres" : "lowres") + ".png"), o.EncodeToPNG());
                UnityEngine.Object.Destroy(o);
                Main.Log("CAL sheet written, window origin (" + ox + "," + oy + ") size " + C);
            }
        }

        static double Slope(int xi, int yi, out double corr)
        {
            int n = rows.Count;
            double mx = 0, my = 0;
            foreach (var r in rows) { mx += r[xi]; my += r[yi]; }
            mx /= n; my /= n;
            double sxx = 0, sxy = 0, syy = 0;
            foreach (var r in rows) { double dx = r[xi] - mx, dy = r[yi] - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
            corr = (sxx > 0 && syy > 0) ? sxy / Math.Sqrt(sxx * syy) : 0;
            return sxx > 0 ? sxy / sxx : 0;
        }

        static void Report()
        {
            if (rows.Count < 8) { Main.Log("CAL not enough samples (" + rows.Count + ")"); return; }
            double c1, c2, c3, c4;
            double sxx = Slope(0, 2, out c1), syy = Slope(1, 3, out c2), sxy = Slope(1, 2, out c3), syx = Slope(0, 3, out c4);
            Main.Log("CAL RESULT n=" + rows.Count + "  d(cx)/d(jx)=" + sxx.ToString("F3") + " (r=" + c1.ToString("F2") + ")"
                + "  d(cy)/d(jy)=" + syy.ToString("F3") + " (r=" + c2.ToString("F2") + ")"
                + "  d(cx)/d(jy)=" + sxy.ToString("F3") + " (r=" + c3.ToString("F2") + ")"
                + "  d(cy)/d(jx)=" + syx.ToString("F3") + " (r=" + c4.ToString("F2") + ")");
        }
    }
}
