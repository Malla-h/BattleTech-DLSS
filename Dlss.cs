using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace BTScale
{
    // Managed side of BTDLSS.dll (native NGX bridge). All D3D11 work happens on the render thread via plugin events.
    public static class Dlss
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("BTDLSS", CharSet = CharSet.Unicode)] static extern void BTDLSS_SetLogPath(string path);
        [DllImport("BTDLSS")] static extern IntPtr BTDLSS_GetEventFunc();
        [DllImport("BTDLSS")] static extern int BTDLSS_GetStatus();
        [DllImport("BTDLSS")] static extern uint BTDLSS_GetLastResult();
        [DllImport("BTDLSS")] static extern int BTDLSS_GetEvalCount();
        [DllImport("BTDLSS")] static extern int BTDLSS_StructSizes(int which);
        [DllImport("BTDLSS")] static extern int BTDLSS_GetVramMB(out ulong usageMB, out ulong budgetMB);
        [DllImport("BTDLSS")] static extern void BTDLSS_ResetEvalTiming();
        [DllImport("BTDLSS")] static extern int BTDLSS_GetEvalTiming(out ulong totalUs, out ulong count);

        // Average GPU milliseconds the DLSS evaluate call took since ResetTiming (from timestamp queries around the call).
        public static void ResetTiming() { if (loaded) try { BTDLSS_ResetEvalTiming(); } catch { } }
        public static bool AvgEvalMs(out double ms, out ulong samples)
        {
            ms = 0; samples = 0;
            if (!loaded) return false;
            try { ulong us; if (BTDLSS_GetEvalTiming(out us, out samples) != 1 || samples == 0) return false; ms = us / (double)samples / 1000.0; return true; }
            catch { return false; }
        }

        // VRAM this process uses and the budget the OS gives it. Only available once the native plugin has a device.
        public static bool VramMB(out ulong usage, out ulong budget)
        {
            usage = budget = 0;
            if (!loaded) return false;
            try { return BTDLSS_GetVramMB(out usage, out budget) == 1; } catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CreateData
        {
            public IntPtr anyTexture;
            public int renderW, renderH, outW, outH, quality, flags, preset;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string dir;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct EvalData
        {
            public IntPtr color, depth, motion, output;
            public float jitterX, jitterY, mvScaleX, mvScaleY;
            public int reset, renderW, renderH;
            public float sharpness, preExposure;
        }

        // NVSDK_NGX_DLSS_Feature_Flags
        const int FlagHDR = 1 << 0, FlagMVLowRes = 1 << 1, FlagDepthInverted = 1 << 3, FlagAutoExposure = 1 << 6;
        const int QualityMax = 2;          // NVSDK_NGX_PerfQuality_Value_MaxQuality
        const int PresetK = 11;            // transformer model

        enum St { Off, Creating, Ready, Failed }

        public static bool Enabled = true;
        public static bool NoJitter;                 // isolation test: no jitter at all
        public static int Quality = QualityMax;      // NVSDK_NGX_PerfQuality_Value, set from the menu
        public static int Preset = PresetK;          // NVSDK_NGX_DLSS_Hint_Render_Preset, 0 = NGX default
        public static float OutlineSign = -1f;       // direction of the fallback mech-outline shift (confirmed better than +1)
        public static string LastFailure = "";
        public static RenderTexture OutRT, MidRT;   // OutRT: UAV, DLSS output. MidRT: end of the PostProcessing chain, fed to BTPostProcess.
        public static Vector2 JitterPx;
        public static int Phases = 18;
        // Sign conventions between Unity and DLSS, toggled live with Ctrl+F1..F4. Both confirmed by eye: jitter -1,-1 and motion vectors -1,-1.
        public static float JitSx = -1f, JitSy = -1f, MvSx = -1f, MvSy = -1f;
        public static int LastEvalFrame = -100;
        public static int MidFrame = -1;                                    // frame in which MidRT holds a finished image

        // Camera cuts: DLSS history from the old view would smear into the new one for a few frames, so the next evaluate is told to reset.
        // The game announces its own cuts through TaaComponent.ResetHistory (hooked in PPPatches). The rest is a safety net for jumps it does not
        // announce: a very large rotation in one frame, or a large move that is also implausibly fast.
        public static bool ResetPending;
        const float CutAngle = 30f, CutDistance = 40f, CutSpeed = 600f;
        static Vector3 lastCamPos; static Quaternion lastCamRot; static float lastCamTime = -1f;
        static int resetLogs;

        public static void RequestReset(string why)
        {
            ResetPending = true;
            if (resetLogs++ < 30) Main.Log("DLSS history reset requested: " + why);
        }

        static void NoteCamera(Camera cam)
        {
            var t = cam.transform;
            float now = Time.unscaledTime;
            if (lastCamTime >= 0f)
            {
                float dt = Mathf.Max(now - lastCamTime, 1e-4f);
                float dist = Vector3.Distance(t.position, lastCamPos);
                float ang = Quaternion.Angle(t.rotation, lastCamRot);
                if (ang > CutAngle) RequestReset("camera rotated " + ang.ToString("F0") + " degrees in one frame");
                else if (dist > CutDistance && dist / dt > CutSpeed) RequestReset("camera moved " + dist.ToString("F0") + " units in " + (dt * 1000f).ToString("F0") + " ms");
            }
            lastCamPos = t.position; lastCamRot = t.rotation; lastCamTime = now;
        }

        static readonly int CreateSize = Marshal.SizeOf(typeof(CreateData)), EvalSize = Marshal.SizeOf(typeof(EvalData));
        static St state = St.Off;
        static bool loaded;
        static IntPtr evalFn, evalRing, createRing;
        static int slot, cw, ch, ow, oh, cq, cp, createFrame, jitterIndex;

        // Forget a previous failure so changed settings get another try (the failure may have been a bad combination).
        public static void Retry() { if (state == St.Failed) { state = St.Off; LastFailure = ""; } }
        static CommandBuffer cb;

        public static bool Ready { get { return Enabled && state == St.Ready; } }

        public static string Describe()
        {
            if (!Main.S.debug) return "DLSS " + (Enabled ? state.ToString() : "disabled");
            return "DLSS " + (Enabled ? state.ToString() : "disabled") + (loaded ? " status=" + BTDLSS_GetStatus() + " evals=" + BTDLSS_GetEvalCount() : "")
                + " jit(" + JitSx + "," + JitSy + ")" + (NoJitter ? "[OFF]" : "") + " mv(" + MvSx + "," + MvSy + ") mip=" + MipBias.Current.ToString("F2") + " outl=" + (Main.S.fullResOutlines ? "fullres" : "lowres");
        }

        public static bool Failed { get { return state == St.Failed; } }

        static bool Load()
        {
            // The DLSS runtime is NVIDIA's file and is not part of the download; say so plainly if it was not added.
            if (!File.Exists(Path.Combine(Main.Dir, "nvngx_dlss.dll")))
            {
                LastFailure = "nvngx_dlss.dll is missing from the BTScale mod folder. It is NVIDIA's file and is not included in the download: see the README (Install, step 2).";
                Main.Log(LastFailure);
                return false;
            }
            try
            {
                var path = Path.Combine(Main.Dir, "BTDLSS.dll");
                if (LoadLibraryW(path) == IntPtr.Zero) { Main.Log("LoadLibrary failed for " + path + " err=" + Marshal.GetLastWin32Error()); return false; }
                BTDLSS_SetLogPath(Path.Combine(Main.Dir, "BTDLSS.log"));
                int sc = BTDLSS_StructSizes(0), se = BTDLSS_StructSizes(1);
                if (sc != Marshal.SizeOf(typeof(CreateData)) || se != Marshal.SizeOf(typeof(EvalData)))
                {
                    Main.Log("Struct size mismatch: native " + sc + "/" + se + " managed " + Marshal.SizeOf(typeof(CreateData)) + "/" + Marshal.SizeOf(typeof(EvalData)));
                    return false;
                }
                evalFn = BTDLSS_GetEventFunc();
                evalRing = Marshal.AllocHGlobal(se * 16);
                createRing = Marshal.AllocHGlobal(sc * 4);
                cb = new CommandBuffer { name = "BTDLSS" };
                loaded = true;
                Main.Log("BTDLSS loaded");
                return true;
            }
            catch (Exception e) { Main.Log("BTDLSS load exception: " + e); return false; }
        }

        static void Issue(int id, IntPtr data)
        {
            cb.Clear();
            cb.IssuePluginEventAndData(evalFn, id, data);
            Graphics.ExecuteCommandBuffer(cb);
        }

        public static void EnsureTargets(int w, int h)
        {
            if (OutRT == null || OutRT.width != w || OutRT.height != h)
            {
                if (OutRT != null) OutRT.Release();
                OutRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { enableRandomWrite = true, name = "BTDLSS Out" };
                OutRT.Create();
            }
            if (MidRT == null || MidRT.width != w || MidRT.height != h)
            {
                if (MidRT != null) MidRT.Release();
                MidRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "BTDLSS Mid" };
                MidRT.Create();
            }
        }

        // Called every frame the scaled combat camera renders. Drives load, (re)creation and readiness.
        public static void Tick(int rw, int rh, int outW, int outH, RenderTexture anyTexture)
        {
            if (!Enabled || state == St.Failed) return;
            if (!loaded && !Load()) { state = St.Failed; return; }
            EnsureTargets(outW, outH);

            if (state == St.Creating)
            {
                if (Time.frameCount - createFrame < 3) return;      // let the render thread run the create event first
                int s = BTDLSS_GetStatus();
                if (s == 1) { state = St.Ready; Main.Log("DLSS ready"); }
                else if (s < 0)
                {
                    state = St.Failed;
                    LastFailure = "status " + s + " result 0x" + BTDLSS_GetLastResult().ToString("X8");
                    Main.Log("DLSS failed: " + LastFailure);
                }
                return;
            }
            if (state == St.Ready && cw == rw && ch == rh && ow == outW && oh == outH && cq == Quality && cp == Preset) return;

            var cd = new CreateData
            {
                anyTexture = anyTexture.GetNativeTexturePtr(),
                renderW = rw, renderH = rh, outW = outW, outH = outH,
                quality = Quality,
                flags = FlagHDR | FlagMVLowRes | FlagDepthInverted | FlagAutoExposure,
                preset = Preset,
                dir = Main.Dir
            };
            var p = createRing + (slot++ & 3) * CreateSize;
            Marshal.StructureToPtr(cd, p, false);
            Issue(1, p);
            cw = rw; ch = rh; ow = outW; oh = outH; cq = Quality; cp = Preset;
            createFrame = Time.frameCount;
            state = St.Creating;
            Phases = Mathf.Clamp(Mathf.CeilToInt(8f * (outW / (float)rw) * (outW / (float)rw)), 8, 32);
            jitterIndex = 0;
            Main.Log("DLSS create issued " + rw + "x" + rh + " -> " + outW + "x" + outH + " quality " + Quality + " preset " + Preset + " phases " + Phases);
        }

        static float Halton(int index, int radix)
        {
            float r = 0f, f = 1f / radix;
            for (int i = index; i > 0; i /= radix) { r += f * (i % radix); f /= radix; }
            return r;
        }

        // Installed as PostProcessingBehaviour.jitteredMatrixFunc: our own jitter instead of the game's weak 8-sample one.
        public static Matrix4x4 JitterMatrix(Vector2 ignored)
        {
            var cam = Scaler.MainCam;
            NoteCamera(cam);
            var m = cam.projectionMatrix;
            jitterIndex = (jitterIndex + 1) % Phases;
            JitterPx = NoJitter ? Vector2.zero : new Vector2(Halton(jitterIndex + 1, 2) - 0.5f, Halton(jitterIndex + 1, 3) - 0.5f);
            // The camera always gets the raw jitter. JitSx/JitSy only change what DLSS is told (see Evaluate).
            m[0, 2] += JitterPx.x * 2f / cam.pixelWidth;
            m[1, 2] += JitterPx.y * 2f / cam.pixelHeight;
            return m;
        }

        public static bool Evaluate(RenderTexture color, RenderTexture depth, RenderTexture motion, RenderTexture output, int rw, int rh)
        {
            if (state != St.Ready || color == null || depth == null || motion == null || output == null) return false;
            bool reset = ResetPending || Time.frameCount - LastEvalFrame > 1;
            ResetPending = false;
            var ed = new EvalData
            {
                color = color.GetNativeTexturePtr(), depth = depth.GetNativeTexturePtr(),
                motion = motion.GetNativeTexturePtr(), output = output.GetNativeTexturePtr(),
                jitterX = JitSx * JitterPx.x, jitterY = JitSy * JitterPx.y,
                mvScaleX = MvSx * rw, mvScaleY = MvSy * rh,
                reset = reset ? 1 : 0, renderW = rw, renderH = rh,
                sharpness = 0f, preExposure = 1f
            };
            var p = evalRing + (slot++ & 15) * EvalSize;
            Marshal.StructureToPtr(ed, p, false);
            Issue(2, p);
            LastEvalFrame = Time.frameCount;
            return true;
        }
    }
}
