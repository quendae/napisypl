[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$helperSource = Join-Path $repoRoot 'tools\offline-mt\bergamot-helper'
$msvcPatch = Join-Path $helperSource 'patches\sentencepiece-modern-msvc.patch'
$pcrePatch = Join-Path $helperSource 'patches\ssplit-pcre2-cmake-policy.patch'

if (Test-Path $SourceDirectory) {
    Remove-Item $SourceDirectory -Recurse -Force
}
if (Test-Path $BuildDirectory) {
    Remove-Item $BuildDirectory -Recurse -Force
}

$sourceParent = Split-Path $SourceDirectory -Parent
$buildParent = Split-Path $BuildDirectory -Parent
New-Item -ItemType Directory -Path $sourceParent -Force | Out-Null
New-Item -ItemType Directory -Path $buildParent -Force | Out-Null

Invoke-Checked -Description 'Clone pinned Bergamot v0.4.5' -Command {
    git clone --branch v0.4.5 --depth 1 --recurse-submodules --shallow-submodules `
        https://github.com/browsermt/bergamot-translator.git $SourceDirectory
}

$sentencepiece = Join-Path $SourceDirectory '3rd_party\marian-dev\src\3rd_party\sentencepiece'
Invoke-Checked -Description 'Validate SentencePiece MSVC patch' -Command {
    git -C $sentencepiece apply --check $msvcPatch
}
Invoke-Checked -Description 'Apply SentencePiece MSVC patch' -Command {
    git -C $sentencepiece apply $msvcPatch
}

$ssplit = Join-Path $SourceDirectory '3rd_party\ssplit-cpp'
Invoke-Checked -Description 'Validate ssplit PCRE2 patch' -Command {
    git -C $ssplit apply --check $pcrePatch
}
Invoke-Checked -Description 'Apply ssplit PCRE2 patch' -Command {
    git -C $ssplit apply $pcrePatch
}

Copy-Item $helperSource (Join-Path $SourceDirectory 'subflow-helper') -Recurse
Add-Content (Join-Path $SourceDirectory 'CMakeLists.txt') "`nadd_subdirectory(subflow-helper)"

$cmakeArgs = @(
    '-S', $SourceDirectory,
    '-B', $BuildDirectory,
    '-A', 'x64',
    '-DBUILD_ARCH=x86-64-v2',
    '-DCMAKE_POLICY_VERSION_MINIMUM=3.5',
    '-DSSPLIT_USE_INTERNAL_PCRE2=ON',
    '-DCOMPILE_TESTS=OFF',
    '-DCOMPILE_UNIT_TESTS=OFF',
    '-DCOMPILE_PYTHON=OFF'
)
Invoke-Checked -Description 'Configure Bergamot Windows x64' -Command {
    cmake @cmakeArgs
}

Invoke-Checked -Description 'Build SubFlow.BergamotHelper' -Command {
    cmake --build $BuildDirectory --config Release --target SubFlow.BergamotHelper --parallel 2
}

$helper = Get-ChildItem $BuildDirectory -Filter 'SubFlow.BergamotHelper.exe' -Recurse | Select-Object -First 1
if (-not $helper) {
    throw 'SubFlow.BergamotHelper.exe was not produced.'
}

$outputDirectory = Split-Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
Copy-Item $helper.FullName $OutputPath -Force

$resolvedOutput = (Resolve-Path $OutputPath).Path
Write-Host "Bergamot helper: $resolvedOutput"
Get-Item $resolvedOutput | Select-Object FullName, Length
