[CmdletBinding()]
param(
    [string]$Destination = '',
    [switch]$Force,
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Set-StrictMode -Version Latest

# Windows PowerShell 5.1 does not reliably initialize $PSScriptRoot while
# evaluating default parameter expressions. Resolve the script directory only
# after the param block so launching through powershell.exe -File works too.
$ScriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptRoot) -and
    -not [string]::IsNullOrWhiteSpace([string]$MyInvocation.MyCommand.Path)) {
    $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
}
if ([string]::IsNullOrWhiteSpace($ScriptRoot)) {
    throw "Could not resolve the SubFlow AMD installer directory."
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $ScriptRoot "nllb-amd-runtime"
}

# Keep this runtime aligned with the RX 6950 XT stack already hardware-accepted
# in StableAMD. The private NuGet Python distribution includes a normal pip
# environment; unlike python.org's embeddable ZIP it does not need get-pip.py.
$PythonVersion = "3.12.10"
$PythonDistribution = "nuget-x64"
$PythonPackageUrl = "https://api.nuget.org/v3-flatcontainer/python/$PythonVersion/python.$PythonVersion.nupkg"
$AmdIndex = "https://rocm.nightlies.amd.com/whl-multi-arch/"
$GfxTarget = "gfx1030"
$TorchVersion = "2.13.0+rocm10.1.0a20260822"
$TorchSpec = "torch[device-$GfxTarget]==$TorchVersion"
$TransformersVersion = "4.57.6"
$SentencePieceVersion = "0.2.2"
$SafeTensorsVersion = "0.6.2"
$HelperSource = Join-Path $ScriptRoot "nllb_helper.py"
$RuntimeManifest = Join-Path $Destination "runtime.json"
$PythonExe = Join-Path $Destination "python.exe"
$InstalledHelper = Join-Path $Destination "nllb_helper.py"

$plan = [pscustomobject]@{
    PythonVersion = $PythonVersion
    PythonDistribution = $PythonDistribution
    PythonPackageUrl = $PythonPackageUrl
    GfxTarget = $GfxTarget
    AmdIndex = $AmdIndex
    Torch = $TorchVersion
    TorchPackage = $TorchSpec
    Transformers = $TransformersVersion
    Destination = $Destination
}

if ($PlanOnly) {
    $plan | Format-List | Out-Host
    return $plan
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "SubFlow AMD runtime bootstrap is Windows-only."
}

if (-not (Test-Path $HelperSource -PathType Leaf)) {
    throw "Missing nllb_helper.py next to the installer: $HelperSource"
}

function Invoke-CheckedNative {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $oldEap = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldEap
    }

    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Invoke-GpuProbe {
    param([Parameter(Mandatory = $true)][string]$PythonPath)

    $probePath = Join-Path $env:TEMP ("subflow-amd-probe-{0}.py" -f [Guid]::NewGuid().ToString("N"))
    @'
import json
import torch
import transformers
import sentencepiece
import safetensors

info = {
    "torch_version": torch.__version__,
    "hip": torch.version.hip,
    "gpu_available": bool(torch.cuda.is_available()),
    "device_name": torch.cuda.get_device_name(0) if torch.cuda.is_available() else "CPU",
    "fp16_matmul_ok": False,
    "transformers_version": transformers.__version__,
}

if torch.cuda.is_available():
    a = torch.randn((256, 256), device="cuda", dtype=torch.float16)
    b = a @ a
    torch.cuda.synchronize()
    info["fp16_matmul_ok"] = list(b.shape) == [256, 256]

print(json.dumps(info, ensure_ascii=False))
'@ | Set-Content -Path $probePath -Encoding UTF8

    $oldOverride = [Environment]::GetEnvironmentVariable("HSA_OVERRIDE_GFX_VERSION", "Process")
    $hadOverride = $null -ne $oldOverride
    Remove-Item Env:HSA_OVERRIDE_GFX_VERSION -ErrorAction SilentlyContinue
    try {
        $raw = @(& $PythonPath -s $probePath 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($hadOverride) { $env:HSA_OVERRIDE_GFX_VERSION = $oldOverride }
        else { Remove-Item Env:HSA_OVERRIDE_GFX_VERSION -ErrorAction SilentlyContinue }
        Remove-Item $probePath -Force -ErrorAction SilentlyContinue
    }

    $raw | ForEach-Object { Write-Host $_ }
    $jsonLine = $raw | Where-Object { $_.Trim().StartsWith("{") -and $_.Trim().EndsWith("}") } | Select-Object -Last 1
    $parsed = $null
    if ($jsonLine) {
        try { $parsed = $jsonLine | ConvertFrom-Json } catch { $parsed = $null }
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Parsed = $parsed
        Passed = (
            $exitCode -eq 0 -and
            $null -ne $parsed -and
            [bool]$parsed.gpu_available -and
            [bool]$parsed.fp16_matmul_ok -and
            [string]$parsed.torch_version -eq $TorchVersion
        )
    }
}

function Test-ManagedRuntimeManifest {
    if (-not (Test-Path $RuntimeManifest -PathType Leaf)) { return $false }
    try {
        $manifest = Get-Content -Path $RuntimeManifest -Raw | ConvertFrom-Json
        return (
            [string]$manifest.python -eq $PythonVersion -and
            [string]$manifest.pythonDistribution -eq $PythonDistribution -and
            [string]$manifest.torch -eq $TorchVersion -and
            [string]$manifest.target -eq $GfxTarget
        )
    }
    catch {
        return $false
    }
}

Write-Host "SubFlow AMD GPU runtime installer" -ForegroundColor Cyan
Write-Host "Target: $Destination"
Write-Host "GPU target: $GfxTarget / Radeon RX 6950 XT"
Write-Host "Runtime: private CPython $PythonVersion ($PythonDistribution)"
Write-Host "PyTorch: $TorchVersion"
Write-Host "Index: $AmdIndex"
Write-Host ""

if (-not $Force -and (Test-ManagedRuntimeManifest) -and (Test-Path $PythonExe -PathType Leaf) -and (Test-Path $InstalledHelper -PathType Leaf)) {
    Write-Host "Checking existing managed AMD runtime before downloading anything..." -ForegroundColor Cyan
    $existingProbe = Invoke-GpuProbe -PythonPath $PythonExe
    if ($existingProbe.Passed) {
        Copy-Item $HelperSource $InstalledHelper -Force
        Write-Host "Existing AMD runtime is healthy: $($existingProbe.Parsed.device_name)" -ForegroundColor Green
        Write-Host "Restart SubFlow and choose Fast, Balanced or Quality."
        return
    }
    Write-Warning "Existing AMD runtime is unhealthy or no longer matches the pinned RX 6950 XT stack. It will be repaired."
}

if (Test-Path $Destination) {
    Write-Host "Resetting managed AMD runtime..." -ForegroundColor Yellow
    try {
        Remove-Item $Destination -Recurse -Force
    }
    catch {
        throw "Could not reset '$Destination'. Close SubFlow and any Python process using this runtime, then run the installer again. $($_.Exception.Message)"
    }
}
New-Item -ItemType Directory -Force $Destination | Out-Null

$tempRoot = Join-Path $env:TEMP ("subflow-amd-runtime-{0}" -f [Guid]::NewGuid().ToString("N"))
$pythonPackage = Join-Path $tempRoot "python.$PythonVersion.nupkg.zip"
$pythonExtract = Join-Path $tempRoot "python-extracted"
New-Item -ItemType Directory -Force $tempRoot | Out-Null
New-Item -ItemType Directory -Force $pythonExtract | Out-Null

try {
    Write-Host "Downloading private CPython $PythonVersion NuGet runtime..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -Uri $PythonPackageUrl -OutFile $pythonPackage

    Write-Host "Extracting private Python runtime..."
    Expand-Archive -Path $pythonPackage -DestinationPath $pythonExtract -Force
    $pythonToolsRoot = Join-Path $pythonExtract "tools"
    $packagePython = Join-Path $pythonToolsRoot "python.exe"
    if (-not (Test-Path $packagePython -PathType Leaf)) {
        throw "CPython NuGet package did not contain expected executable '$packagePython'."
    }

    Get-ChildItem -Path $pythonToolsRoot -Force | ForEach-Object {
        Move-Item -LiteralPath $_.FullName -Destination $Destination
    }
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $PythonExe -PathType Leaf)) {
    throw "Private CPython runtime did not produce expected executable '$PythonExe'."
}

Write-Host "Verifying bundled pip: python -m pip --version..." -ForegroundColor Cyan
Invoke-CheckedNative -FilePath $PythonExe -Arguments @("-m", "pip", "--version") -Description "bundled pip verification"

Write-Host "Installing pinned TheRock PyTorch stack for $GfxTarget..." -ForegroundColor Cyan
Invoke-CheckedNative -FilePath $PythonExe -Arguments @(
    "-m", "pip", "install", "--pre", "--disable-pip-version-check", "--no-cache-dir",
    "--index-url", $AmdIndex,
    $TorchSpec
) -Description "AMD TheRock PyTorch installation"

Write-Host "Installing Transformers runtime without replacing PyTorch..." -ForegroundColor Cyan
Invoke-CheckedNative -FilePath $PythonExe -Arguments @(
    "-m", "pip", "install", "--disable-pip-version-check", "--no-cache-dir",
    "transformers==$TransformersVersion",
    "sentencepiece==$SentencePieceVersion",
    "safetensors==$SafeTensorsVersion"
) -Description "Transformers runtime installation"

Invoke-CheckedNative -FilePath $PythonExe -Arguments @("-m", "pip", "check") -Description "pip check"
Copy-Item $HelperSource $InstalledHelper -Force

Write-Host "Running real FP16 Radeon compute probe..." -ForegroundColor Cyan
$probe = Invoke-GpuProbe -PythonPath $PythonExe
if (-not $probe.Passed) {
    $details = if ($probe.Parsed) {
        "torch=$($probe.Parsed.torch_version), hip=$($probe.Parsed.hip), gpu=$($probe.Parsed.gpu_available), device=$($probe.Parsed.device_name), fp16=$($probe.Parsed.fp16_matmul_ok)"
    } else {
        "probe did not return valid JSON (exit code $($probe.ExitCode))"
    }
    throw "AMD runtime installed, but the RX 6950 XT FP16 validation failed: $details"
}

$runtimeInfo = [ordered]@{
    schemaVersion = 2
    python = $PythonVersion
    pythonDistribution = $PythonDistribution
    torch = $TorchVersion
    indexUrl = $AmdIndex
    target = $GfxTarget
    transformers = $TransformersVersion
    device = [string]$probe.Parsed.device_name
    hip = [string]$probe.Parsed.hip
    fp16Matmul = [bool]$probe.Parsed.fp16_matmul_ok
    installedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
}
$runtimeInfo | ConvertTo-Json -Depth 6 | Set-Content $RuntimeManifest -Encoding UTF8

Write-Host ""
Write-Host "AMD GPU runtime is ready: $($probe.Parsed.device_name)" -ForegroundColor Green
Write-Host "PyTorch: $($probe.Parsed.torch_version) · HIP: $($probe.Parsed.hip) · FP16: OK"
Write-Host "Restart SubFlow and choose Fast, Balanced or Quality."
