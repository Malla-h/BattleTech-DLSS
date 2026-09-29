# Assembles a clean, shippable BTScale folder in .\dist\BTScale (only the files the mod needs; no logs, captures or user settings).
# nvngx_dlss.dll comes from NVIDIA's DLSS SDK. It is copied only if -IncludeDlss is given; check NVIDIA's license before sharing it.
param([switch]$IncludeDlss)
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
Copy-Item (Join-Path $root "..\BTDLSS\bin\BTDLSS.dll") $out
if ($IncludeDlss) { Copy-Item (Join-Path $root "..\ThirdParty\DLSS\lib\Windows_x86_64\rel\nvngx_dlss.dll") $out }

Get-ChildItem $out | Select-Object Name, Length
