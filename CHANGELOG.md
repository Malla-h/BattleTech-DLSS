# Changelog

## 0.9.0
First complete version.
- Low-resolution combat render with DLSS upscaling, full-resolution HUD and post-process.
- Settings menu (F11): quality mode from DLAA to Ultra Performance, DLSS preset (Default/J/K/L/M), texture sharpness bias, saved to
  `BTScale.user.json`.
- Corrected mouse picking, hover, nameplates and edge-of-screen indicators at the internal resolution.
- Own jitter (Halton, sized for the render scale) and confirmed sign conventions for jitter and motion vectors.
- Texture mip bias for the lower render resolution; transparents and VFX use the jittered projection.
- Mech outline / move-cursor texture handled to stay aligned; small residual shimmer remains.
- Developer tools kept behind `"debug": true`.
- Logs rotate per session and are size-capped. Per-frame lookups cached.
- Transparents are no longer jittered by default (it made mech outlines wobble); optional via `jitterTransparents`.
- Depth-reconstruction matrices (`_BT_InvVP`, `_BT_ViewProjection`, `_GlobalProjection`) are re-published after the jitter so decals and
  boundary lines no longer shimmer as a whole (`syncJitterMatrices`).
- Debug benchmark: all quality modes and DLSS presets, average/1 % low FPS, VRAM and DLSS GPU time.
