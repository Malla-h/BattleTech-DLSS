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
- Mech outlines, the move cursor and the mission boundary are rendered at output resolution (`fullResOutlines`, default on), so they
  no longer look like a low-resolution render next to the DLSS image and are much steadier. Costs some VRAM (about 100 MB).
- Fixed hover and click on units outside the bottom-left part of the screen: Unity's PhysicsRaycaster rejects pointers outside
  `camera.pixelRect`, which was only the internal-resolution rectangle.
- DLSS history is reset on camera cuts (the game's own temporal-reset signal, plus a conservative jump detector).
- Removed the experimental matrix-sync and unjitter code (no visible effect).
- Hotkeys: only the menu key (F11) is bound by default, to stay clear of the F-keys players use for quick save/load and unit selection.
  `toggleKey` and the new `dlssKey` are unbound by default and configurable (`"None"` = unbound); everything is also in the menu.
