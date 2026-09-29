# BTScale: DLSS for BattleTech

Renders the combat camera at a lower internal resolution and upscales it to your screen resolution with NVIDIA DLSS. The HUD,
tooltips and post-processing stay at full resolution. Requires ModTek (the game's HBS mod loader alone is not enough because the
mod uses Harmony patches).

## Requirements
- An NVIDIA RTX GPU with a recent driver.
- BattleTech with ModTek installed (tested with ModTek 4.5.2, Unity 2018.4, Direct3D 11).
- `nvngx_dlss.dll` (DLSS 310.9.1 was used for development) placed in the mod folder. It is not included in this repository; it comes
  from NVIDIA's DLSS SDK (https://github.com/NVIDIA/DLSS, `lib/Windows_x86_64/rel/`). Check NVIDIA's license before redistributing it.

## Install
Copy the `BTScale` folder into `BATTLETECH\Mods\`. It must contain `BTScale.dll`, `BTDLSS.dll`, `nvngx_dlss.dll` and `mod.json`.
Start the game and confirm BTScale is enabled in the mod manager.

## Use
| Key | Action | Default |
|---|---|---|
| `menuKey` | Open the settings menu (drag the window; **Save** keeps your choices) | **F11** |
| `dlssKey` | DLSS on/off (off falls back to a plain stretch of the low-res image, useful for comparing) | unbound |
| `toggleKey` | Whole render-scale pipeline on/off (off is the vanilla renderer) | unbound |
| `hideUiKey` | Hide the whole game UI (and this mod's own text) for clean screenshots | unbound |

Only the menu is bound by default, because players commonly use F1 to F6, F8 and F9 for quick save, quick load and unit selection. Every
setting, including both toggles, is in the menu. To bind a key, set it in `mod.json` (or `BTScale.user.json`) to a Unity key name such as
`"F7"` or `"Insert"`; `"None"` leaves it unbound. If `menuKey` is unbound or invalid it falls back to F11.

Menu options:
- **Quality mode**: DLAA (native resolution), Ultra Quality (77 %), Quality (66.7 %), Balanced (58 %), Performance (50 %),
  Ultra Performance (33 %). Higher percentages look better and cost more GPU time.
- **DLSS preset**: Default lets NVIDIA choose per mode. K is the transformer model and the default here. L and M are heavier.
- **Texture sharpness bias**: compensates for texture blur at lower internal resolutions (Full, 2/3, 1/3, Off).
- **Hide the game UI**: removes the HUD, tooltips, outlines and this mod's text so you can take clean screenshots. Close the menu to
  see the result; open it again to switch the UI back on. It works with or without the render-scale pipeline. Bind `hideUiKey` for a hotkey.

Settings are stored in `BTScale.user.json` in the mod folder (it overrides `mod.json`). Delete it to reset.

## Performance (measured)
RTX 5060, 3840x2160, one GPU-bound combat view, DLSS preset K. GPU time is the DLSS pass alone, from timestamp queries.

| Mode | Frame time | FPS | DLSS pass |
|---|---|---|---|
| Native (mod off) | 17.7 ms | 56 | - |
| DLAA | 21.5 ms | 47 | 3.7 ms |
| Ultra Quality | 16.4 ms | 61 | 3.6 ms |
| Quality | 14.2 ms | 71 | 3.6 ms |
| Balanced | 12.8 ms | 79 | 3.6 ms |
| Performance | 11.4 ms | 88 | 3.6 ms |
| Ultra Performance | 10.9 ms | 92 | 3.4 ms |

Presets cost very different amounts. At Quality: K and J 3.6 ms, M 7.5 ms, L 9.7 ms. At DLAA, M costs 13.7 ms. K is the default for that reason.
Video memory use drops (about 3.3 GB at Quality against 4.1 GB native). In CPU-limited scenes several modes read the same FPS because
the game is not waiting on the GPU there.

## Notes and known limitations
- Only the combat camera is scaled. Menus, the star map and the mech bay are unchanged.
- Mech outlines, the move cursor and mission-boundary lines are rendered at output resolution (menu option, on by default) and
  composited after DLSS. They keep the game's own hard 1-pixel edge look, and can still flicker slightly where they cross depth edges
  (rocks, the treeline), as they do without DLSS. Jittering transparents would make them worse, so that is off by default.
- The volumetric fog is applied before DLSS and has no motion vectors of its own, so it can lag slightly during fast camera moves.
- Particle resolution follows the game's own effects-quality setting.
- If the game's own anti-aliasing option is off, DLSS does not run (the game's TAA path is what enables the jitter DLSS needs).
- Logs: `BTScale.log` and `BTDLSS.log` in the mod folder (the previous session's are kept as `.old`).
- Setting `"debug": true` in `mod.json` or `BTScale.user.json` enables developer tools (sign-flip keys, stage captures, 4K screenshots,
  outline calibration). They are not needed for normal use.

## How it works (short)
The combat camera renders into a low-resolution texture. Harmony patches route the post-processing chain through DLSS (which replaces
the game's TAA step) so that bloom, tonemapping and the UI composite run at output resolution, redirect the final image to a full
resolution target that a small present camera shows, and rescale mouse and world-to-screen conversions so picking and HUD placement stay
correct. `BTDLSS.dll` is a small native plugin that talks to NGX on the render thread.

## Building
- `BTScale`: `dotnet build -c Release` (references the game's `Managed` folder and ModTek's `0Harmony.dll`; adjust `BTRoot` in
  `BTScale.csproj` if your game is elsewhere).
- `BTDLSS` (separate repo/folder): `build.bat` with Visual Studio C++ build tools and the NVIDIA DLSS SDK.
- `package.ps1` assembles a clean folder with only the files listed above.
