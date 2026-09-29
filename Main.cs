using System;
using System.IO;
using System.Reflection;
using BattleTech.Rendering;
using BattleTech.Rendering.UI;
using HarmonyLib;
using UnityEngine;

namespace BTScale
{
    [Serializable]
    public class Settings
    {
        public string quality = "Quality";     // one of Main.QualityNames
        public string preset = "K";            // one of Main.PresetNames
        public int mipLevel = 0;               // index into MipBias.Levels: 0 full, 1 two thirds, 2 a third, 3 off
        public bool dlss = true;
        public bool debug = false;             // developer tools: sign-flip keys, stage captures, screenshots, calibration
        public string toggleKey = "F8";        // whole render pipeline on/off
        public string menuKey = "F11";
    }

    public static class Main
    {
        internal static string Dir;
        internal static Settings S = new Settings();

        // NVIDIA's standard ratios, NGX PerfQuality_Value in the third column.
        internal static readonly string[] QualityNames = { "DLAA", "Ultra Quality", "Quality", "Balanced", "Performance", "Ultra Performance" };
        internal static readonly float[] QualityRatio = { 1f, 0.77f, 0.6667f, 0.58f, 0.5f, 0.3333f };
        // Ultra Quality uses the Quality value: this NGX rejects its own UltraQuality value (0xBAD00010, unsupported parameter),
        // but accepts any render size in range, so the 77% size still applies.
        internal static readonly int[] QualityNgx = { 5, 2, 2, 1, 0, 3 };
        internal static readonly string[] PresetNames = { "Default", "J", "K", "L", "M" };
        internal static readonly int[] PresetValue = { 0, 10, 11, 12, 13 };

        internal static string UserPath { get { return Path.Combine(Dir, "BTScale.user.json"); } }

        internal static int QualityIndex
        {
            get { int i = Array.IndexOf(QualityNames, S.quality); return i < 0 ? 2 : i; }
        }
        internal static int PresetIndex
        {
            get { int i = Array.IndexOf(PresetNames, S.preset); return i < 0 ? 2 : i; }
        }
        internal static float Ratio { get { return QualityRatio[QualityIndex]; } }

        internal static void ApplyToRuntime()
        {
            Dlss.Enabled = S.dlss;
            Dlss.Quality = QualityNgx[QualityIndex];
            Dlss.Preset = PresetValue[PresetIndex];
            MipBias.SetLevel(S.mipLevel);
        }

        internal static void Save()
        {
            try { File.WriteAllText(UserPath, JsonUtility.ToJson(S, true)); Log("Settings saved"); } catch (Exception e) { Log("Save failed: " + e.Message); }
        }

        public static void Init(string directory, string settingsJSON)
        {
            Dir = directory;
            RotateLog("BTScale.log");
            RotateLog("BTDLSS.log");
            try { if (!string.IsNullOrEmpty(settingsJSON)) JsonUtility.FromJsonOverwrite(settingsJSON, S); } catch { }
            try { if (File.Exists(UserPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(UserPath), S); } catch { }
            S.mipLevel = Mathf.Clamp(S.mipLevel, 0, 3);
            ApplyToRuntime();
            try
            {
                var harmony = new Harmony("btscale");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                CamPatches.Apply(harmony,
                    typeof(BattleTech.CameraControl),
                    typeof(BattleTech.MouseRotation),
                    typeof(BattleTech.Rendering.RegionRenderer),
                    typeof(UnityEngine.EventSystems.PhysicsRaycaster));
                Log("Harmony patches applied");
            }
            catch (Exception e) { Log("PatchAll failed: " + e); }
            var go = new GameObject("BTScale");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Scaler>();
            go.AddComponent<MipBias>();
            go.AddComponent<Menu>();
        }

        // Logs are per session: the previous one is kept as .old, and a session stops logging past a size cap.
        const long LogCap = 2 * 1024 * 1024;
        static long logBytes;
        static bool logCapped;

        internal static void RotateLog(string name)
        {
            try
            {
                var p = Path.Combine(Dir, name);
                if (File.Exists(p))
                {
                    var old = p + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(p, old);
                }
            }
            catch { }
        }

        internal static void Log(string msg)
        {
            if (logCapped) return;
            try
            {
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n";
                if ((logBytes += line.Length) > LogCap) { logCapped = true; line += "-- log size cap reached, further messages dropped --\n"; }
                File.AppendAllText(Path.Combine(Dir, "BTScale.log"), line);
            }
            catch { }
        }
    }

    // Owns the low-resolution target of the combat camera.
    public class Scaler : MonoBehaviour
    {
        internal static RenderTexture LowRT;
        internal static RenderTexture FinalRT;
        internal static bool Enabled = true;
        internal static bool SkipUI;

        KeyCode key = KeyCode.F8;
        string status = "";
        Camera presentCam;
        int stableFrames, shotCount;
        float nextStatusAt;

        void Awake()
        {
            try { key = (KeyCode)Enum.Parse(typeof(KeyCode), Main.S.toggleKey, true); } catch { }

            // Presents FinalRT to the screen. A camera with no target gets a correct full-screen viewport and clear.
            var go = new GameObject("BTScale Present");
            go.transform.SetParent(transform, false);
            presentCam = go.AddComponent<Camera>();
            presentCam.cullingMask = 0;
            // Just above the combat camera (-10): the Presenter blit covers the whole screen, and cameras that draw
            // straight to the screen (loading and menu UI) must still render after it, as they do in vanilla.
            presentCam.clearFlags = CameraClearFlags.Nothing;
            presentCam.depth = -9.5f;
            presentCam.allowHDR = false;
            presentCam.allowMSAA = false;
            presentCam.useOcclusionCulling = false;
            presentCam.renderingPath = RenderingPath.Forward;
            presentCam.enabled = false;
            go.AddComponent<Presenter>();
        }

        internal static RenderTexture GetFinalRT()
        {
            int w = Screen.width, h = Screen.height;
            if (FinalRT == null || FinalRT.width != w || FinalRT.height != h)
            {
                if (FinalRT != null) FinalRT.Release();
                FinalRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                FinalRT.name = "BTScale FinalRT " + w + "x" + h;
                FinalRT.Create();
                Main.Log("Created " + FinalRT.name);
            }
            return FinalRT;
        }

        // Camera.main and component lookups are not free and this runs several times per frame, so they are cached and
        // re-checked about once a second (the combat camera can gain components while a mission loads).
        static Camera mainCam;
        static int mainCamFrame = -1000;
        static bool combatCam;
        static BattleTech.Rendering.BTPostProcess cachedBtpp;
        static UnityEngine.PostProcessing.PostProcessingBehaviour cachedPpb;

        internal static Camera MainCam
        {
            get
            {
                if (mainCam == null || !mainCam.isActiveAndEnabled || Time.frameCount - mainCamFrame > 60)
                {
                    mainCam = Camera.main;
                    mainCamFrame = Time.frameCount;
                    combatCam = FindCombat(mainCam);
                    cachedBtpp = combatCam ? mainCam.GetComponent<BattleTech.Rendering.BTPostProcess>() : null;
                    cachedPpb = mainCam != null ? mainCam.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>() : null;
                }
                return mainCam;
            }
        }

        // The combat camera is the main camera that carries RenderTrees. Menus and the sim game use other setups.
        static bool FindCombat(Camera c)
        {
            if (c == null) return false;
            foreach (var comp in c.GetComponents<Component>())
                if (comp != null && comp.GetType().Name == "RenderTrees") return true;
            return false;
        }

        static bool IsCombatCamera(Camera c) { return c != null && c == MainCam && combatCam; }

        internal static bool Active(Camera c)
        {
            return c != null && LowRT != null && c.targetTexture == LowRT;
        }

        void DebugKeys(bool ctrl)
        {
            if (!ctrl && Input.GetKeyDown(KeyCode.F7)) { Capture.Armed = true; Main.Log("Capture armed"); }
            if (!ctrl && Input.GetKeyDown(KeyCode.F6)) { SkipUI = !SkipUI; Main.Log("SkipUI=" + SkipUI); }
            if (ctrl && Input.GetKeyDown(KeyCode.F1)) { Dlss.MvSx = -Dlss.MvSx; Main.Log("MvSx=" + Dlss.MvSx); }
            if (ctrl && Input.GetKeyDown(KeyCode.F2)) { Dlss.MvSy = -Dlss.MvSy; Main.Log("MvSy=" + Dlss.MvSy); }
            if (ctrl && Input.GetKeyDown(KeyCode.F3)) { Dlss.JitSx = -Dlss.JitSx; Main.Log("JitSx=" + Dlss.JitSx); }
            if (ctrl && Input.GetKeyDown(KeyCode.F4)) { Dlss.JitSy = -Dlss.JitSy; Main.Log("JitSy=" + Dlss.JitSy); }
            if (ctrl && Input.GetKeyDown(KeyCode.F5)) { Dlss.NoJitter = !Dlss.NoJitter; Main.Log("NoJitter=" + Dlss.NoJitter); }
            if (ctrl && Input.GetKeyDown(KeyCode.F6)) MipBias.Cycle();
            if (ctrl && Input.GetKeyDown(KeyCode.F8)) { Dlss.OutlineSign = -Dlss.OutlineSign; Main.Log("OutlineSign=" + Dlss.OutlineSign); }
            if (ctrl && Input.GetKeyDown(KeyCode.F10)) Calib.Start();
            if (ctrl && Input.GetKeyDown(KeyCode.F9)) { PP_OnPreCull_Outline.Unjitter = !PP_OnPreCull_Outline.Unjitter; Main.Log("Outline unjitter=" + PP_OnPreCull_Outline.Unjitter); }
            // Ctrl+F7: full-resolution screenshot of exactly what is on screen, named by rendering mode, for offline comparison.
            if (ctrl && Input.GetKeyDown(KeyCode.F7))
            {
                string mode = !(Enabled && LowRT != null && Camera.main != null && Camera.main.targetTexture == LowRT) ? "native" : (Dlss.Ready ? "dlss" : "bilinear");
                string file = Path.Combine(Main.Dir, "shot_" + mode + "_" + (++shotCount) + ".png");
                ScreenCapture.CaptureScreenshot(file);
                Main.Log("Screenshot requested: " + file);
            }
        }

        void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            // User keys. Plain keys must not fire when the same key is pressed together with Ctrl.
            if (!ctrl && Input.GetKeyDown(key)) { Enabled = !Enabled; Main.Log("Toggled, Enabled=" + Enabled); }
            if (!ctrl && Input.GetKeyDown(KeyCode.F5)) { Dlss.Enabled = !Dlss.Enabled; Main.S.dlss = Dlss.Enabled; Dlss.Retry(); Main.Log("DLSS enabled=" + Dlss.Enabled); }

            // Developer tools, hidden unless "debug": true is set in mod.json or BTScale.user.json.
            //   F7 stage capture, F6 skipUI, Ctrl+F1..F4 motion-vector/jitter sign flips (settled at -1,-1), Ctrl+F5 no jitter,
            //   Ctrl+F6 mip level, Ctrl+F7 4K screenshot, Ctrl+F8 outline shift direction, Ctrl+F9 outline unjitter, Ctrl+F10 calibration.
            if (Main.S.debug) DebugKeys(ctrl);

            var cam = MainCam;
            bool combat = combatCam;
            var pp = cachedBtpp;
            bool loading = pp != null && pp.loadingCam;
            // Wait for the combat camera to settle (and the loading screen to end) before redirecting its output.
            if (combat && !loading) stableFrames++; else stableFrames = 0;
            bool want = Enabled && stableFrames > 30;
            if (presentCam.enabled != want) presentCam.enabled = want;

            // Our own jitter for DLSS; the game's stays untouched whenever DLSS is not running.
            var ppb = cachedPpb;
            if (ppb != null)
            {
                Func<Vector2, Matrix4x4> jf = (want && Dlss.Ready) ? (Func<Vector2, Matrix4x4>)Dlss.JitterMatrix : null;
                if (ppb.jitteredMatrixFunc != jf) ppb.jitteredMatrixFunc = jf;
            }

            if (want)
            {
                int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * Main.Ratio) & ~1);
                int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * Main.Ratio) & ~1);
                if (LowRT == null || LowRT.width != w || LowRT.height != h)
                {
                    if (cam.targetTexture == LowRT) cam.targetTexture = null;
                    if (LowRT != null) LowRT.Release();
                    LowRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    LowRT.name = "BTScale LowRT " + w + "x" + h;
                    LowRT.Create();
                    Main.Log("Created " + LowRT.name);
                }
                if (cam.targetTexture != LowRT)
                {
                    cam.targetTexture = LowRT;
                    Main.Log("Camera '" + cam.name + "' now renders to " + LowRT.name + " (pixel " + cam.pixelWidth + "x" + cam.pixelHeight + ")");
                }
                if (Time.unscaledTime >= nextStatusAt)     // building this string every frame allocates for nothing
                {
                    nextStatusAt = Time.unscaledTime + 0.25f;
                    status = (SkipUI ? "[skipUI] " : "") + "BTScale ON " + LowRT.width + "x" + LowRT.height + " -> " + Screen.width + "x" + Screen.height + " (" + Main.S.toggleKey + " toggles) | " + Dlss.Describe();
                }
            }
            else
            {
                // skipUI is only meant to be forced while our patch is running; never leave it stuck on.
                if (pp != null && pp.skipUI && !SkipUI) pp.skipUI = false;
                if (cam != null && LowRT != null && cam.targetTexture == LowRT)
                {
                    cam.targetTexture = null;
                    Main.Log("Camera restored to screen");
                }
                status = IsCombatCamera(cam) ? "BTScale OFF (" + Main.S.toggleKey + " toggles)" : "";
            }
        }

        void OnGUI()
        {
            if (status.Length > 0) GUI.Label(new Rect(10, 6, 700, 24), status);
        }
    }

    public class Presenter : MonoBehaviour
    {
        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            Graphics.Blit(Scaler.FinalRT != null ? (Texture)Scaler.FinalRT : src, dst);
        }
    }

    // Upscale the world at the last moment: BTPostProcess (bloom, exposure, UI composite, final blit) runs at full resolution
    // on an upscaled copy of its input and writes into FinalRT, which the Presenter camera puts on the screen.
    [HarmonyPatch(typeof(BTPostProcess), "OnRenderImage")]
    static class BTPostProcess_OnRenderImage
    {
        internal class State { public RenderTexture Up, ElemComp, ElemOrig; }
        static readonly int ElemId = Ids.ElementUI;

        static void Prefix(BTPostProcess __instance, ref RenderTexture source, ref RenderTexture destination, out State __state)
        {
            __state = null;
            try
            {
                var cam = __instance.GetComponent<Camera>();
                if (!Scaler.Active(cam)) return;
                __instance.skipUI = Scaler.SkipUI;
                // DLSS path: the PostProcessing stack already produced a full-resolution image. Otherwise stretch the low-res one.
                bool fromDlss = Dlss.MidRT != null && Dlss.MidFrame == Time.frameCount;
                RenderTexture up;
                if (fromDlss) up = Dlss.MidRT;
                else
                {
                    var d = source.descriptor;
                    d.width = Screen.width; d.height = Screen.height;
                    d.depthBufferBits = 0; d.msaaSamples = 1;
                    up = RenderTexture.GetTemporary(d);
                    up.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(source, up);
                }
                __state = new State { Up = fromDlss ? null : up };

                // gameUIRT (mech outlines, move cursor) is camera-sized. The composite maps every UI texture assuming it is
                // screen-sized, so hand it a stretched screen-sized copy for the duration of the pass.
                var em = HBS.LazySingletonBehavior<ElementManager>.Instance;
                var elem = em != null ? em.gameUIRT : null;
                if (elem != null && Calib.Left > 0) Calib.Sample(elem, fromDlss);
                if (elem != null && (elem.width != Screen.width || elem.height != Screen.height))
                {
                    var ec = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    ec.filterMode = FilterMode.Bilinear;
                    ec.wrapMode = TextureWrapMode.Clamp;
                    // The outlines are drawn with the jittered camera but never go through DLSS, so they would visibly wobble.
                    // Sample them shifted by this frame's jitter to cancel it.
                    Vector2 shift = Vector2.zero;
                    if (fromDlss && !PP_OnPreCull_Outline.Unjitter) shift = Dlss.OutlineSign * new Vector2(Dlss.JitterPx.x / elem.width, Dlss.JitterPx.y / elem.height);
                    Graphics.Blit(elem, ec, Vector2.one, shift);
                    Shader.SetGlobalTexture(ElemId, ec);
                    __state.ElemComp = ec;
                    __state.ElemOrig = elem;
                }
                if (Capture.Armed) Capture.Begin(source, up);
                // The composite maps the UI with the built-in _ScreenParams, which still holds the low-res camera size here
                // (Unity only sets it per camera). Post/UI shaders that turn pixel positions into UVs need the real size.
                Ids.SetScreenSizeGlobals(Screen.width, Screen.height);
                source = up;
                destination = Scaler.GetFinalRT();
            }
            catch (Exception e) { Main.Log("BTPostProcess prefix failed: " + e); }
        }

        static Exception Finalizer(State __state, Exception __exception)
        {
            if (__state != null && Capture.Armed) Capture.End();
            if (__state != null)
            {
                if (__state.ElemComp != null)
                {
                    Shader.SetGlobalTexture(ElemId, __state.ElemOrig);
                    RenderTexture.ReleaseTemporary(__state.ElemComp);
                }
                if (__state.Up != null) RenderTexture.ReleaseTemporary(__state.Up);
            }
            if (__exception != null) Main.Log("BTPostProcess.OnRenderImage threw: " + __exception);
            return __exception;
        }
    }

    // The UI render target is sized from the main camera. Keep it at screen size while the world renders low-res.
    [HarmonyPatch(typeof(UICameraRenderer), "CheckRT")]
    static class UICameraRenderer_CheckRT
    {
        static bool Prefix(ref bool __result)
        {
            if (!Scaler.Enabled || Scaler.LowRT == null || Camera.main == null || Camera.main.targetTexture != Scaler.LowRT) return true;
            int w = Screen.width, h = Screen.height;
            var rtField = AccessTools.Field(typeof(UICameraRenderer), "_mainUIRT");
            var rt = (RenderTexture)rtField.GetValue(null);
            __result = false;
            if (rt == null || !rt.IsCreated() || rt.width != w || rt.height != h || rt.antiAliasing != BTCustomRenderer.UIMSAA)
            {
                UnityEngine.Object.DestroyImmediate(rt);
                var n = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                n.name = "Main UI RT " + w + "x" + h;
                n.filterMode = FilterMode.Bilinear;
                n.wrapMode = TextureWrapMode.Clamp;
                n.anisoLevel = 0;
                n.useMipMap = false;
                n.hideFlags = HideFlags.DontSave;
                n.antiAliasing = BTCustomRenderer.UIMSAA;
                rtField.SetValue(null, n);
                n.Create();
                Shader.SetGlobalTexture(UICameraRenderer.mainUI_ID, n);
                __result = true;
                Main.Log("UI RT recreated at screen size: " + n.name);
            }
            return false;
        }
    }
}
