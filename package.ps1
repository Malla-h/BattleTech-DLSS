# Assembles a clean, shippable BTScale folder in .\dist\BTScale: only the files the mod needs, no logs, captures or user settings.
# The release includes NVIDIA's DLSS runtime (nvngx_dlss.dll) and BTDLSS.dll (which contains NVIDIA NGX code), as permitted for the SDK's
# redistributable components inside an application. NVIDIA's license text and a notice therefore travel with the package, and this script
# refuses to build a package without them. Neither NVIDIA file is ever committed to the source repositories.
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

# The NVIDIA files need the NVIDIA license text with them. Refuse to build a package without either the license or the runtime.
$sdk = Join-Path $root "..\ThirdParty\DLSS"
$nvidiaLicense = Join-Path $sdk "LICENSE.txt"
$runtime = Join-Path $sdk "lib\Windows_x86_64\rel\nvngx_dlss.dll"
if (-not (Test-Path $nvidiaLicense)) { throw "NVIDIA SDK license not found at $nvidiaLicense. Clone github.com/NVIDIA/DLSS to ..\ThirdParty\DLSS first." }
if (-not (Test-Path $runtime)) { throw "DLSS runtime not found at $runtime." }
New-Item -ItemType Directory -Force (Join-Path $out "licenses") | Out-Null
Copy-Item $nvidiaLicense (Join-Path $out "licenses\NVIDIA-RTX-SDKs-LICENSE.txt")
Copy-Item $runtime $out

Get-ChildItem $out -Recurse -File | Select-Object @{n="Path";e={$_.FullName.Substring($out.Length + 1)}}, Length
Write-Host "Note: the package contains NVIDIA files (nvngx_dlss.dll, BTDLSS.dll); NVIDIA's license text is in licenses\."
