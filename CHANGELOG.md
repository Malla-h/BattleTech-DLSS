# Changelog

## 0.9.2
- The release download now includes NVIDIA's DLSS runtime (`nvngx_dlss.dll`, version 310.9.1), so installing is just extracting the folder. It ships with
  NVIDIA's license text and a notice, as `BTDLSS.dll` already did. The source repositories still contain no NVIDIA files.
- Updated the install instructions and the message shown if the runtime file is missing.

## 0.9.1
- Clearer install instructions, with a direct link to the single `nvngx_dlss.dll` file players have to get from NVIDIA and how to check it.
- If `nvngx_dlss.dll` is missing (or DLSS otherwise cannot run), the mod now falls back to the game's normal renderer with a short on-screen message
  and an explanation in the menu, instead of showing a blurry low-resolution stretch. Adding the file and changing any setting picks it up without a
  restart.

## 0.9.0
First complete version.
- Low-resolution combat render with DLSS upscaling, full-resolution HUD and post-process.
- Settings menu (F11): quality mode from DLAA to Ultra Performance, DLSS preset (Default/J/K/L/M), texture sharpness bias, saved to
  `BTScale.user.json`.
- Corrected mouse picking, hover, nameplates and edge-of-screen indicators at the internal resolution.
- Own jitter (Halton, sized for the render scale) and confirmed sign conventions for jitter and motion vectors.
- Texture mip bias for the lower render resolution; transparents and VFX use the jittered projection.
- Mech outline / move-cursor texture handled to stay aligned.
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
- NVIDIA attribution: the menu shows a "Uses NVIDIA DLSS" credit, and the download carries `THIRD-PARTY-NOTICES.md` and NVIDIA's license text
  (`BTDLSS.dll` contains NVIDIA NGX code). `nvngx_dlss.dll` is not included.
- Release DLLs no longer embed the build machine's folder path.
- New "Hide the game UI" option (menu checkbox and optional `hideUiKey`) for clean screenshots, no longer a debug-only tool. It only
  writes the game's own `skipUI` flag while it is holding it, so the game's debug fly-camera use of that flag is not disturbed.
