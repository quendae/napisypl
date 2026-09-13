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

# Firefox currently vendors this exact mozilla/translations revision as bergamot-translator v0.6.0.
# Generation-3 Firefox models from translations-models-v2 require the newer engine; the old
# browsermt/bergamot-translator v0.4.5 helper can terminate while loading those models.
$bergamotRevision = 'eea6e5a80aa4ddd86d9cc35ce9a65b79aa3ab96d'

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

Invoke-Checked -Description 'Clone Mozilla Translations' -Command {
    git clone --filter=blob:none --no-checkout https://github.com/mozilla/translations.git $SourceDirectory
}
Invoke-Checked -Description 'Fetch pinned Firefox Bergamot revision' -Command {
    git -C $SourceDirectory fetch --depth 1 origin $bergamotRevision
}
Invoke-Checked -Description 'Checkout pinned Firefox Bergamot revision' -Command {
    git -C $SourceDirectory checkout --detach FETCH_HEAD
}

$requiredSubmodules = @(
    'inference/3rd_party/ssplit-cpp',
    'inference/marian-fork/src/3rd_party/intgemm',
    'inference/marian-fork/src/3rd_party/sentencepiece',
    'inference/marian-fork/src/3rd_party/simd_utils'
)
Invoke-Checked -Description 'Initialize Bergamot inference submodules' -Command {
    git -C $SourceDirectory submodule update --init --depth 1 --recursive -- @requiredSubmodules
}

# ssplit-cpp at this Firefox revision downloads PCRE2 10.39 as an ExternalProject.
# CMake 4.x removed pre-3.5 policy compatibility, so pass the same compatibility floor
# to the nested PCRE2 configure invocation as we pass to the top-level project.
# On MSVC, PCRE2 10.39 names the static library pcre2-8-static.lib, while this old
# FindPCRE2.cmake assumes pcre2-8.lib. Patch that stale assumption before configuring.
$pcreFindPath = Join-Path $SourceDirectory 'inference\3rd_party\ssplit-cpp\cmake\FindPCRE2.cmake'
$pcreFind = Get-Content $pcreFindPath -Raw
$pcreNeedle = '    -DCMAKE_POSITION_INDEPENDENT_CODE:BOOL=true # Added for pybind11'
if (-not $pcreFind.Contains($pcreNeedle)) {
    throw 'Unexpected ssplit-cpp FindPCRE2.cmake layout; cannot apply CMake 4 compatibility fix safely.'
}
$pcreFind = $pcreFind.Replace(
    $pcreNeedle,
    "    -DCMAKE_POLICY_VERSION_MINIMUM=3.5`r`n$pcreNeedle")

$pcreLibraryNeedle = '  set(PCRE2_LIBRARIES ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/${CMAKE_STATIC_LIBRARY_PREFIX}pcre2-8${CMAKE_STATIC_LIBRARY_SUFFIX})'
if (-not $pcreFind.Contains($pcreLibraryNeedle)) {
    throw 'Unexpected ssplit-cpp FindPCRE2.cmake library naming; cannot apply MSVC static-library fix safely.'
}
$pcreLibraryReplacement = @'
  if(MSVC)
    set(PCRE2_LIBRARIES ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/${CMAKE_STATIC_LIBRARY_PREFIX}pcre2-8-static${CMAKE_STATIC_LIBRARY_SUFFIX})
  else()
    set(PCRE2_LIBRARIES ${CMAKE_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/${CMAKE_STATIC_LIBRARY_PREFIX}pcre2-8${CMAKE_STATIC_LIBRARY_SUFFIX})
  endif()
'@
$pcreFind = $pcreFind.Replace($pcreLibraryNeedle, $pcreLibraryReplacement.TrimEnd())
Set-Content -Path $pcreFindPath -Value $pcreFind -Encoding utf8NoBOM

$inferenceSource = Join-Path $SourceDirectory 'inference'
Copy-Item $helperSource (Join-Path $inferenceSource 'subflow-helper') -Recurse
Add-Content (Join-Path $inferenceSource 'CMakeLists.txt') "`nadd_subdirectory(subflow-helper)"

$cmakeArgs = @(
    '-S', $inferenceSource,
    '-B', $BuildDirectory,
    '-A', 'x64',
    '-DBUILD_ARCH=x86-64-v2',
    '-DCMAKE_POLICY_VERSION_MINIMUM=3.5',
    '-DGIT_SUBMODULE=OFF',
    '-DSSPLIT_USE_INTERNAL_PCRE2=ON',
    '-DUSE_DOXYGEN=OFF',
    '-DCOMPILE_TESTS=OFF',
    '-DCOMPILE_UNIT_TESTS=OFF',
    '-DCOMPILE_PYTHON=OFF'
)
Invoke-Checked -Description 'Configure Firefox-compatible Bergamot Windows x64' -Command {
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
