# BTScale: DLSS for BattleTech

Adds NVIDIA DLSS and DLAA to BattleTech's combat view. With DLSS the camera renders at a lower internal resolution and is upscaled to your
screen resolution; with DLAA it renders at your full resolution and DLSS is used purely as anti-aliasing (higher quality, higher GPU cost). The HUD,
tooltips and post-processing stay at full resolution. Requires [ModTek](https://github.com/BattletechModders/ModTek/releases), the community mod
loader, because the mod uses Harmony 2 patches that the base game does not include.

## Related projects
- [BattleTech-DLSS-Native](https://github.com/Malla-h/BattleTech-DLSS-Native): the small native plugin (`BTDLSS.dll`) this mod uses to talk to NVIDIA's
  DLSS. Its build instructions live there.
- [BattleTech-FarSight](https://github.com/Malla-h/BattleTech-FarSight): a separate mod that lengthens shrub and tree draw distance. It works
  alongside this one.

## Requirements
- An NVIDIA RTX GPU with a recent driver.
- BattleTech with **ModTek** installed (tested with ModTek 4.5.2, Unity 2018.4, Direct3D 11). Install step 1 below says how to get it if you don't have it.
- NVIDIA's DLSS runtime, `nvngx_dlss.dll`, comes **with the release download** (see Credits and licenses). This source repository does not contain it.

## Install
1. **Install ModTek (skip this if you already have it).** Download `ModTek.zip` from the [ModTek releases page](https://github.com/BattletechModders/ModTek/releases)
   and extract it into the folder that contains `BattleTech.exe`. That adds `winhttp.dll` next to the executable and a `Mods` folder. When it works,
   the main menu shows `/W MODTEK` next to the version number. ModTek's own notes say the game must be installed outside `Program Files`
   (Windows' UAC restrictions interfere). See [ModTek's INSTALL.md](https://github.com/BattletechModders/ModTek/blob/master/INSTALL.md) for details.
2. **Get the mod.** Download `BTScale-<version>.zip` from the [Releases page](https://github.com/Malla-h/BattleTech-DLSS/releases) and extract the
   `BTScale` folder into `BATTLETECH\Mods\`. It already contains everything: `BTScale.dll`, `BTDLSS.dll`, `nvngx_dlss.dll` (DLSS 310.9.1) and `mod.json`,
   plus the license and notice files.
3. **Start the game** and make sure BTScale is enabled in the mod manager (restart the game after enabling it). Load a mission and press **F11**. The menu
   shows `DLSS Ready` once DLSS is running.

**If it doesn't work:** if `nvngx_dlss.dll` is missing (an antivirus may remove it, or the folder was copied incompletely), the menu says so and the
game keeps its normal renderer, so nothing looks worse. Re-extract the mod, then change any setting in the menu (for example toggle DLSS off and on) and it is
picked up without restarting. Otherwise check `BTScale.log` and `BTDLSS.log` in the mod folder; an issue report with those two files is the most useful
thing you can send.

**Updating the DLSS runtime (optional).** The mod was tested with the bundled 310.9.1. NVIDIA publishes newer runtimes in its DLSS SDK repository
(https://github.com/NVIDIA/DLSS, `lib/Windows_x86_64/rel/nvngx_dlss.dll`); replacing the file is possible but untested with this mod, and NVIDIA's license
applies to whichever version you use.

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

## Planned
AMD FSR upscaling support is planned for a future version. There is no date, and it is not part of the current release.

## Notes and known limitations
- Only the combat camera is scaled. Menus, the star map and the mech bay are unchanged.
- Mission-boundary lines are rendered at output resolution (menu option, on by default) and composited after DLSS, so they can flicker slightly where
  they cross depth edges (rocks, the treeline), as they do without DLSS. Jittering transparents (a debug setting) would make the mech outlines wobble,
  so it is off by default.
- The fog is applied before DLSS and has no motion vectors of its own, so it can lag slightly during fast camera moves.
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
- `BTScale`: `dotnet build -c Release`. It references the game's `Managed` folder and ModTek's `0Harmony.dll`. Point it at your game with the
  `BATTLETECH_DIR` environment variable, `-p:BTRoot=...`, or a git-ignored `local.props` file (see the comment in `BTScale.csproj`). Without any of
  these it looks in the default Steam location.
- `BTDLSS.dll` (the native plugin) comes from its own repository, [BattleTech-DLSS-Native](https://github.com/Malla-h/BattleTech-DLSS-Native): run its
  `build.bat` with the Visual Studio C++ build tools and the NVIDIA DLSS SDK. See its README.
- `package.ps1` assembles a clean release folder (the mod, `BTDLSS.dll`, NVIDIA's `nvngx_dlss.dll`, the notices and NVIDIA's license text). It expects a clone
  of the native plugin repository next to this one in a folder named `BTDLSS`, and NVIDIA's DLSS SDK in `..\ThirdParty\DLSS` (the runtime and license text come
  from there). It refuses to build a package without the license text.

## Credits and licenses
- BTScale is released under the [MIT License](LICENSE). That covers the code in this repository only.
- **DLSS is a technology of NVIDIA Corporation.** This mod is unofficial and not sponsored or endorsed by NVIDIA. NVIDIA, DLSS, RTX and GeForce RTX
  are trademarks of NVIDIA Corporation.
- This source repository contains no NVIDIA files. The prebuilt release download includes two NVIDIA-licensed files: `nvngx_dlss.dll` (NVIDIA's DLSS
  runtime) and `BTDLSS.dll` (which contains statically linked NVIDIA NGX code). They are provided under the NVIDIA RTX SDKs License; the license text
  and a notice come with the download (`licenses/` and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)).
- [Harmony](https://github.com/pardeike/Harmony) does the runtime patching, and ModTek loads the mod. Neither is included here.
- BattleTech is a game by Harebrained Schemes / Paradox. This is an unofficial fan modification.
