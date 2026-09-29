using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace BTScale
{
    // The combat camera renders into a texture smaller than the screen, and Unity then reports its screen space in
    // texture pixels. The game feeds it real mouse pixels (and expects real pixels back), so we rescale at the call sites.
    public static class CamProxy
    {
        static bool Scaled(Camera c, out float kx, out float ky)
        {
            kx = ky = 1f;
            if (!Scaler.Active(c)) return false;
            kx = (float)c.pixelWidth / Screen.width;
            ky = (float)c.pixelHeight / Screen.height;
            return true;
        }

        // Unity's PhysicsRaycaster discards any pointer outside camera.pixelRect before casting. With the camera rendering into a smaller
        // texture that rect covers only the bottom-left part of the screen, so hover and clicks died outside it.
        public static Rect PixelRect(Camera c)
        {
            float kx, ky;
            return Scaled(c, out kx, out ky) ? new Rect(0f, 0f, Screen.width, Screen.height) : c.pixelRect;
        }

        public static Ray ScreenPointToRay(Camera c, Vector3 p)
        {
            float kx, ky;
            if (Scaled(c, out kx, out ky)) { p.x *= kx; p.y *= ky; }
            return c.ScreenPointToRay(p);
        }

        public static Vector3 ScreenToWorldPoint(Camera c, Vector3 p)
        {
            float kx, ky;
            if (Scaled(c, out kx, out ky)) { p.x *= kx; p.y *= ky; }
            return c.ScreenToWorldPoint(p);
        }

        public static Vector3 WorldToScreenPoint(Camera c, Vector3 w)
        {
            var r = c.WorldToScreenPoint(w);
            float kx, ky;
            if (Scaled(c, out kx, out ky)) { r.x /= kx; r.y /= ky; }
            return r;
        }
    }

    static class CamPatches
    {
        static readonly Dictionary<string, MethodInfo> Map = new Dictionary<string, MethodInfo>
        {
            { "ScreenPointToRay", typeof(CamProxy).GetMethod("ScreenPointToRay") },
            { "ScreenToWorldPoint", typeof(CamProxy).GetMethod("ScreenToWorldPoint") },
            { "WorldToScreenPoint", typeof(CamProxy).GetMethod("WorldToScreenPoint") },
            { "get_pixelRect", typeof(CamProxy).GetMethod("PixelRect") },
        };

        static MethodInfo Replacement(object operand)
        {
            var m = operand as MethodInfo;
            if (m == null || m.DeclaringType != typeof(Camera)) return null;
            MethodInfo r;
            if (!Map.TryGetValue(m.Name, out r)) return null;
            var ps = m.GetParameters();
            if (m.Name == "get_pixelRect") return ps.Length == 0 ? r : null;
            return ps.Length == 1 && ps[0].ParameterType == typeof(Vector3) ? r : null;
        }

        public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                var r = (ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) ? Replacement(ins.operand) : null;
                if (r != null) { ins.opcode = OpCodes.Call; ins.operand = r; }
                yield return ins;
            }
        }

        public static void Apply(Harmony harmony, params Type[] types)
        {
            foreach (var type in types)
            {
                if (type == null) continue;
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Cast<MethodBase>()
                    .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
                foreach (var m in methods)
                {
                    try
                    {
                        if (m.IsAbstract || m.GetMethodBody() == null) continue;
                        if (!PatchProcessor.GetCurrentInstructions(m).Any(i => Replacement(i.operand) != null)) continue;
                        harmony.Patch(m, transpiler: new HarmonyMethod(typeof(CamPatches), nameof(Transpile)));
                        Main.Log("Camera-space patch: " + type.Name + "." + m.Name);
                    }
                    catch (Exception e) { Main.Log("Camera-space patch failed for " + type.Name + "." + m.Name + ": " + e.Message); }
                }
            }
        }
    }
}
