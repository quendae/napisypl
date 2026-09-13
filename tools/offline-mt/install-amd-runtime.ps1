param(
    [string]$Destination = (Join-Path $PSScriptRoot "nllb-amd-runtime"),
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$PythonVersion = "3.12.10"
$PythonUrl = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
$GetPipUrl = "https://bootstrap.pypa.io/get-pip.py"
$AmdIndex = "https://stable.repo.amd.com/rocm/whl-next/"
$TorchSpec = "torch[device-gfx1030]==2.13.0+rocm10.0.0"
$HelperSource = Join-Path $PSScriptRoot "nllb_helper.py"

function Invoke-Checked([scriptblock]$Command, [string]$Description) {
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Write-Host "SubFlow AMD GPU runtime installer" -ForegroundColor Cyan
Write-Host "Target: $Destination"
Write-Host "GPU target: gfx1030 / Radeon RX 6000 series"

if (-not (Test-Path $HelperSource)) {
    throw "Missing nllb_helper.py next to the installer: $HelperSource"
}

if ($Force -and (Test-Path $Destination)) {
    Write-Host "Removing previous AMD runtime..."
    Remove-Item $Destination -Recurse -Force
}
New-Item -ItemType Directory -Force $Destination | Out-Null

$PythonExe = Join-Path $Destination "python.exe"
if (-not (Test-Path $PythonExe)) {
    $tempZip = Join-Path $env:TEMP "subflow-python-$PythonVersion-embed-amd64.zip"
    Write-Host "Downloading portable Python $PythonVersion..."
    Invoke-WebRequest -UseBasicParsing -Uri $PythonUrl -OutFile $tempZip
    Expand-Archive -Path $tempZip -DestinationPath $Destination -Force
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
}

$pth = Join-Path $Destination "python312._pth"
if (-not (Test-Path $pth)) {
    throw "Portable Python path file not found: $pth"
}
$pthLines = @(
    "python312.zip",
    ".",
    "Lib\\site-packages",
    "import site"
)
Set-Content -Path $pth -Value $pthLines -Encoding ASCII
New-Item -ItemType Directory -Force (Join-Path $Destination "Lib\\site-packages") | Out-Null

$getPip = Join-Path $env:TEMP "subflow-get-pip.py"
Write-Host "Preparing pip..."
Invoke-WebRequest -UseBasicParsing -Uri $GetPipUrl -OutFile $getPip
Invoke-Checked { & $PythonExe $getPip --disable-pip-version-check } "pip bootstrap"
Remove-Item $getPip -Force -ErrorAction SilentlyContinue

Write-Host "Installing AMD ROCm/TheRock PyTorch for gfx1030..." -ForegroundColor Cyan
Invoke-Checked {
    & $PythonExe -m pip install --disable-pip-version-check --no-cache-dir `
        --index-url $AmdIndex `
        $TorchSpec
} "AMD PyTorch installation"

Write-Host "Installing Transformers runtime..."
Invoke-Checked {
    & $PythonExe -m pip install --disable-pip-version-check --no-cache-dir `
        "transformers==4.57.6" `
        "sentencepiece==0.2.2" `
        "safetensors==0.6.2"
} "Transformers runtime installation"

Invoke-Checked { & $PythonExe -m pip check } "pip check"
Copy-Item $HelperSource (Join-Path $Destination "nllb_helper.py") -Force

$probe = @'
import json
import torch
info = {
    "torch": torch.__version__,
    "gpu_available": bool(torch.cuda.is_available()),
    "device": torch.cuda.get_device_name(0) if torch.cuda.is_available() else "CPU",
}
if torch.cuda.is_available():
    a = torch.randn((256, 256), device="cuda", dtype=torch.float16)
    b = a @ a
    info["fp16_matmul"] = list(b.shape)
print(json.dumps(info, ensure_ascii=False))
'@
Write-Host "Validating runtime..."
$probeOutput = & $PythonExe -c $probe
if ($LASTEXITCODE -ne 0) {
    throw "AMD runtime probe failed with exit code $LASTEXITCODE."
}
$probeOutput | Write-Host

$runtimeInfo = [ordered]@{
    python = $PythonVersion
    torch = $TorchSpec
    target = "gfx1030"
    installedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
}
$runtimeInfo | ConvertTo-Json | Set-Content (Join-Path $Destination "runtime.json") -Encoding UTF8

if ($probeOutput -notmatch '"gpu_available": true') {
    Write-Warning "Runtime installed, but PyTorch did not see an AMD GPU. SubFlow will fall back to CPU until the driver/runtime is visible."
} else {
    Write-Host "AMD GPU runtime is ready. Restart SubFlow and choose Fast, Balanced or Quality." -ForegroundColor Green
}
