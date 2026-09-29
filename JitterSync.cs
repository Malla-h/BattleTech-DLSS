using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BTScale
{
    // BTCustomRenderer.OnPreCull publishes _BT_InvVP, _BT_ViewProjection and _GlobalProjection from camera.projectionMatrix, but that
    // runs before the PostProcessing stack applies the jitter. Depth and the G-buffer are rendered with the jittered matrix, so
    // everything that rebuilds world positions from depth (deferred decals, the mission-area boundary decal, fog, ...) is off by up to half
    // a pixel, differently every frame, and shimmers. Re-publishing the three values after the jitter makes them agree with the depth.
    [HarmonyPatch(typeof(PostProcessingBehaviour), "OnPreCull")]
    static class PP_OnPreCull_SyncMatrices
    {
        static readonly int InvVP = Shader.PropertyToID("_BT_InvVP");
        static readonly int ViewProjection = Shader.PropertyToID("_BT_ViewProjection");
        static readonly int GlobalProjection = Shader.PropertyToID("_GlobalProjection");

        static void Postfix(PostProcessingBehaviour __instance)
        {
            try
            {
                if (!Main.S.syncJitterMatrices) return;
                var cam = __instance.GetComponent<Camera>();
                if (!Scaler.Active(cam) || !Dlss.Ready) return;
                var view = cam.worldToCameraMatrix;
                var vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * view;
                Shader.SetGlobalMatrix(InvVP, vp.inverse);
                Shader.SetGlobalMatrix(ViewProjection, vp);
                var dir = view.MultiplyVector(cam.ViewportPointToRay(new Vector3(1f, 1f, 0f)).direction);
                dir /= Mathf.Abs(dir.z);
                Shader.SetGlobalVector(GlobalProjection, dir);
            }
            catch (Exception e) { Main.Log("Jitter matrix sync failed: " + e.Message); Main.S.syncJitterMatrices = false; }
        }
    }
}
