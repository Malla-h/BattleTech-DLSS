using System;
using BattleTech.Rendering;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace BTScale
{
    // DLSS replaces the PostProcessing stack's TAA step. From there on the stack (bloom, DOF, uber) runs at output resolution,
    // its result lands in Dlss.MidRT, and the BTPostProcess patch takes that as its source.
    [HarmonyPatch(typeof(PostProcessingBehaviour), "OnRenderImage")]
    static class PP_OnRenderImage
    {
        internal static bool WantOut;

        static void Prefix(PostProcessingBehaviour __instance, RenderTexture source, ref RenderTexture destination)
        {
            WantOut = false;
            try
            {
                var cam = __instance.GetComponent<Camera>();
                if (!Scaler.Active(cam)) return;
                Dlss.Tick(source.width, source.height, Screen.width, Screen.height, source);
                if (!Dlss.Ready) return;
                var taa = Traverse.Create(__instance).Field("m_Taa").GetValue<TaaComponent>();
                if (taa == null || !taa.active || !__instance.useAntiAliasing || BTScreenShot.screenshotInProgress) return;
                destination = Dlss.MidRT;
                Dlss.MidFrame = Time.frameCount;
                WantOut = true;
            }
            catch (Exception e) { Main.Log("PP prefix failed: " + e); }
        }

        static Exception Finalizer(Exception __exception)
        {
            WantOut = false;
            if (__exception != null) Main.Log("PostProcessingBehaviour.OnRenderImage threw: " + __exception);
            return __exception;
        }
    }

    // The TAA's output buffer is the first temporary the stack asks for. Hand it the DLSS output instead.
    [HarmonyPatch(typeof(RenderTextureFactory), "Get", new[] { typeof(RenderTexture) })]
    static class RTFactory_Get
    {
        static bool Prefix(RenderTexture baseRenderTexture, ref RenderTexture __result)
        {
            if (!PP_OnRenderImage.WantOut || baseRenderTexture == null || Dlss.OutRT == null) return true;
            PP_OnRenderImage.WantOut = false;
            __result = Dlss.OutRT;
            return false;
        }
    }

    [HarmonyPatch(typeof(TaaComponent), "Render")]
    static class Taa_Render
    {
        static bool Prefix(RenderTexture source, RenderTexture destination)
        {
            if (destination == null || destination != Dlss.OutRT || !Dlss.Ready) return true;
            try
            {
                // Everything after this point works at output resolution; shaders reading the built-in screen size must agree.
                float sw = Screen.width, sh = Screen.height;
                Shader.SetGlobalVector("_ScreenParams", new Vector4(sw, sh, 1f + 1f / sw, 1f + 1f / sh));
                Shader.SetGlobalVector("_ScreenSize", new Vector4(sw, sh, 1f / sw, 1f / sh));
                Shader.SetGlobalFloat("_BTScreenWidth", sw);
                Shader.SetGlobalFloat("_BTScreenHeight", sh);

                var mv = Shader.GetGlobalTexture("_CameraMotionVectorsTexture") as RenderTexture;
                var depth = Shader.GetGlobalTexture("_CameraDepthTexture") as RenderTexture;
                if (Capture.Armed) Capture.DumpMotion(mv, source.width);
                if (!Dlss.Evaluate(source, depth, mv, destination, source.width, source.height))
                    Graphics.Blit(source, destination);      // inputs missing this frame: plain stretch
            }
            catch (Exception e)
            {
                Main.Log("DLSS evaluate path failed: " + e);
                Graphics.Blit(source, destination);
            }
            return false;
        }
    }
}
