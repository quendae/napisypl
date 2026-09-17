# Builds SubFlow-Setup-<version>.exe.
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0
# Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $PSScriptRoot "out"
$app = Join-Path $out "app"

if (Test-Path $app) { Remove-Item -Recurse -Force $app }
New-Item -ItemType Directory -Force $app | Out-Null

Write-Host "[1/3] Publishing SubFlow $Version (self-contained, win-x64)..."
dotnet publish (Join-Path $root "src\NapisyPL\NapisyPL.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:DebugType=none `
    -o $app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "[2/3] Adding the offline translator setup scripts..."
$setup = Join-Path $app "offline-mt-setup"
New-Item -ItemType Directory -Force $setup | Out-Null
Copy-Item (Join-Path $root "tools\offline-mt\install-amd-runtime.ps1") $setup
Copy-Item (Join-Path $root "tools\offline-mt\nllb_helper.py") $setup

Write-Host "[3/3] Compiling the installer..."
$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup"
}

& $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot "SubFlow.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed." }
Write-Host "Done: $(Join-Path $out "SubFlow-Setup-$Version.exe")"
