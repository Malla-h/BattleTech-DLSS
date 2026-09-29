using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech.Rendering;
using BattleTech.Rendering.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering;

namespace BTScale
{
    // The mech outlines, move cursor and mission boundary are drawn by ElementManager into gameUIRT, which the game sizes from the camera.
    // With a low internal resolution that overlay is low-res and unfiltered, and it is composited after DLSS, so it looks like a native
    // 1440p (or lower) render with no anti-aliasing next to a DLSS-reconstructed scene. These patches render it at output resolution.
    public static class ElemFullRes
    {
        public static bool Active { get { return Main.S.fullResOutlines && Scaler.Active(Scaler.MainCam); } }
        public static int Width() { return Active ? Screen.width : -1; }     // -1 is the game's "camera size"
        public static int Height() { return Active ? Screen.height : -1; }
    }

    [HarmonyPatch(typeof(ElementManager), "ValidateGameUIRT")]
    static class ElementManager_ValidateGameUIRT
    {
        static readonly FieldInfo RtField = AccessTools.Field(typeof(ElementManager), "_gameUIRT");

        static bool Prefix(ElementManager __instance)
        {
            if (!ElemFullRes.Active) return true;           // original: camera-sized
            int w = Screen.width, h = Screen.height;
            var rt = RtField.GetValue(__instance) as RenderTexture;
            if (rt == null || !rt.IsCreated() || rt.width != w || rt.height != h || rt.antiAliasing != BTCustomRenderer.UIMSAA)
            {
                UnityEngine.Object.DestroyImmediate(rt);
                var n = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Game UI RT " + w + "x" + h + " (BTScale)",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                    useMipMap = false,
                    hideFlags = HideFlags.DontSave,
                    antiAliasing = BTCustomRenderer.UIMSAA
                };
                n.Create();
                RtField.SetValue(__instance, n);
                Main.Log("Element UI RT recreated at output size: " + n.name);
            }
            return false;
        }
    }

    // RefreshCommandBufferInt records GetTemporaryRT(_BT_Temp01, -1, -1, 24, ...) where -1 means "camera size". That temporary holds the
    // silhouettes before the edge filter runs, so it must match gameUIRT. The two -1 arguments become calls that return the output size
    // while the full-resolution overlay is active (and -1 otherwise, so the original behaviour is untouched).
    [HarmonyPatch(typeof(ElementManager), "RefreshCommandBufferInt")]
    static class ElementManager_RefreshCommandBufferInt
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            var width = AccessTools.Method(typeof(ElemFullRes), nameof(ElemFullRes.Width));
            var height = AccessTools.Method(typeof(ElemFullRes), nameof(ElemFullRes.Height));
            int patched = 0;
            for (int i = 0; i + 2 < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldc_I4_M1 && list[i + 1].opcode == OpCodes.Ldc_I4_M1
                    && (list[i + 2].opcode == OpCodes.Ldc_I4_S || list[i + 2].opcode == OpCodes.Ldc_I4)
                    && Convert.ToInt32(list[i + 2].operand) == 24)
                {
                    var labels = list[i].labels;
                    list[i] = new CodeInstruction(OpCodes.Call, width) { labels = labels };
                    list[i + 1] = new CodeInstruction(OpCodes.Call, height);
                    patched++;
                }
            }
            Main.Log("Element UI buffer transpiler: patched " + patched + " temporary target size(s)");
            return list;
        }
    }

    // While the overlay is rendered at output size, shaders that turn pixel positions into UVs with the built-in / HBS screen-size globals
    // must see the output size, not the camera's. Two tiny buffers around the game's UI buffer set it and put the camera's values back.
    [HarmonyPatch(typeof(PostProcessingBehaviour), "OnPreCull")]
    static class PP_OnPreCull_ScreenParams
    {
        const CameraEvent Evt = CameraEvent.AfterForwardAlpha;   // where BTCustomRenderer attaches the element UI buffer
        static readonly FieldInfo UiField = AccessTools.Field(typeof(ElementManager), "_uiCommandBuffer");
        static CommandBuffer before, after;
        static Camera attachedTo;
        static bool logged;

        static void Detach()
        {
            if (attachedTo != null)
            {
                if (before != null) attachedTo.RemoveCommandBuffer(Evt, before);
                if (after != null) attachedTo.RemoveCommandBuffer(Evt, after);
            }
            attachedTo = null;
        }

        static void Record(CommandBuffer cb, float w, float h)
        {
            cb.Clear();
            cb.SetGlobalVector(Ids.ScreenParams, new Vector4(w, h, 1f + 1f / w, 1f + 1f / h));
            cb.SetGlobalVector(Ids.ScreenSize, new Vector4(w, h, 1f / w, 1f / h));
            cb.SetGlobalFloat(Ids.BTScreenWidth, w);
            cb.SetGlobalFloat(Ids.BTScreenHeight, h);
        }

        static void Postfix(PostProcessingBehaviour __instance)
        {
            try
            {
                var cam = __instance.GetComponent<Camera>();
                if (!ElemFullRes.Active || !Scaler.Active(cam)) { Detach(); return; }
                var em = HBS.LazySingletonBehavior<ElementManager>.Instance;
                var ui = em != null ? UiField.GetValue(em) as CommandBuffer : null;     // the field, not the property: its getter rebuilds the buffer
                if (ui == null) { Detach(); return; }
                if (before == null) { before = new CommandBuffer { name = "BTScale outline screen size" }; after = new CommandBuffer { name = "BTScale outline camera size" }; }

                Record(before, Screen.width, Screen.height);
                Record(after, cam.pixelWidth, cam.pixelHeight);

                // Order: before, the game's UI buffer, after. GetCommandBuffers returns fresh wrapper objects, so match by name.
                var list = cam.GetCommandBuffers(Evt);
                int iu = -1;
                for (int i = 0; i < list.Length; i++) if (list[i].name == ui.name) { iu = i; break; }
                if (iu < 0) { Detach(); return; }
                bool ok = attachedTo == cam && iu > 0 && iu + 1 < list.Length && list[iu - 1].name == before.name && list[iu + 1].name == after.name;
                if (!ok)
                {
                    Detach();
                    cam.RemoveCommandBuffer(Evt, ui);
                    cam.AddCommandBuffer(Evt, before);
                    cam.AddCommandBuffer(Evt, ui);
                    cam.AddCommandBuffer(Evt, after);
                    attachedTo = cam;
                    if (!logged) { logged = true; Main.Log("Full-resolution outlines: screen-size buffers attached around the element UI buffer"); }
                }
            }
            catch (Exception e) { Main.Log("Full-resolution outline setup failed: " + e.Message); Main.S.fullResOutlines = false; Detach(); }
        }
    }
}
