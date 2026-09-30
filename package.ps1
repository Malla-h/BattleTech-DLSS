# Assembles a clean, shippable BTScale folder in .\dist\BTScale: only the files the mod needs, no logs, captures or user settings.
# nvngx_dlss.dll (NVIDIA) is deliberately NOT included: it is governed by NVIDIA's SDK license, so users get it from NVIDIA's SDK repository
# (see the README) and place it in the mod folder themselves.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\BTScale"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Push-Location $root
dotnet build -c Release | Out-Null
Pop-Location

Copy-Item (Join-Path $root "bin\Release\net472\BTScale.dll") $out
Copy-Item (Join-Path $root "mod.json") $out
Copy-Item (Join-Path $root "README.md") $out
Copy-Item (Join-Path $root "CHANGELOG.md") $out
Copy-Item (Join-Path $root "LICENSE") $out
Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $out
Copy-Item (Join-Path $root "..\BTDLSS\bin\BTDLSS.dll") $out

# BTDLSS.dll contains NVIDIA SDK code, so the NVIDIA license text must travel with it. Refuse to build a package without it.
$nvidiaLicense = Join-Path $root "..\ThirdParty\DLSS\LICENSE.txt"
if (-not (Test-Path $nvidiaLicense)) { throw "NVIDIA SDK license not found at $nvidiaLicense. Clone github.com/NVIDIA/DLSS to ..\ThirdParty\DLSS first." }
New-Item -ItemType Directory -Force (Join-Path $out "licenses") | Out-Null
Copy-Item $nvidiaLicense (Join-Path $out "licenses\NVIDIA-RTX-SDKs-LICENSE.txt")

Get-ChildItem $out -Recurse -File | Select-Object @{n="Path";e={$_.FullName.Substring($out.Length + 1)}}, Length
Write-Host "Note: nvngx_dlss.dll is not included. See the README for where to get it."
