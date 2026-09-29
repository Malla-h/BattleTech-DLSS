using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering;
using BattleTech.Rendering.UI;

namespace BTScale
{
    // The mech outlines and move cursor are rasterised at the low render resolution inside the camera render, so with a jittered
    // projection their edges re-rasterise differently every frame and wobble (they never pass through DLSS). Drawing them with the
    // unjittered projection makes them stable. Two small command buffers wrap the game's own UI buffer: one sets the unjittered
    // matrices, one puts the jittered ones back for whatever follows.
    [HarmonyPatch(typeof(PostProcessingBehaviour), "OnPreCull")]
    static class PP_OnPreCull_Outline
    {
        public static bool Unjitter = false;   // the matrix override did not reach the outline drawing (measured); off until calibrated

        const CameraEvent Evt = CameraEvent.AfterForwardAlpha;   // where BTCustomRenderer attaches the element UI buffer
        static readonly System.Reflection.FieldInfo UiField = AccessTools.Field(typeof(ElementManager), "_uiCommandBuffer");
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

        static void Postfix(PostProcessingBehaviour __instance)
        {
            try
            {
                var cam = __instance.GetComponent<Camera>();
                if (!Unjitter || !Scaler.Active(cam) || !Dlss.Ready) { Detach(); return; }
                var em = HBS.LazySingletonBehavior<ElementManager>.Instance;
                var ui = em != null ? UiField.GetValue(em) as CommandBuffer : null;   // not the property: its getter rebuilds the buffer
                if (ui == null) { Detach(); return; }

                if (before == null) { before = new CommandBuffer { name = "BTScale outline unjitter" }; after = new CommandBuffer { name = "BTScale outline rejitter" }; }

                // Matrices for this frame, recorded now: OnPreCull runs after the jitter was applied and before the camera renders.
                before.Clear();
                before.SetViewProjectionMatrices(cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(cam.nonJitteredProjectionMatrix, true));
                after.Clear();
                after.SetViewProjectionMatrices(cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(cam.projectionMatrix, true));

                // Order matters: before, the game's UI buffer, after. GetCommandBuffers returns new wrapper objects on every call,
                // so buffers must be recognised by name, not by reference.
                var list = cam.GetCommandBuffers(Evt);
                int iu = -1;
                for (int i = 0; i < list.Length; i++) if (list[i].name == ui.name) { iu = i; break; }
                if (iu < 0) { Detach(); return; }
                if (!logged) { logged = true; Main.Log("Outline unjitter: element UI buffer '" + ui.name + "' found at index " + iu + " of " + list.Length); }
                bool ok = attachedTo == cam && iu > 0 && iu + 1 < list.Length && list[iu - 1].name == before.name && list[iu + 1].name == after.name;
                if (!ok)
                {
                    Detach();
                    cam.RemoveCommandBuffer(Evt, ui);
                    cam.AddCommandBuffer(Evt, before);
                    cam.AddCommandBuffer(Evt, ui);
                    cam.AddCommandBuffer(Evt, after);
                    attachedTo = cam;
                    Main.Log("Outline unjitter buffers attached around the element UI buffer");
                }
            }
            catch (Exception e) { Main.Log("Outline unjitter failed: " + e.Message); Unjitter = false; Detach(); }
        }
    }
}
