$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcher = Join-Path $repoRoot 'Run-SubFlow.bat'
if (-not (Test-Path $launcher -PathType Leaf)) {
    throw "Run-SubFlow.bat was not found at $launcher"
}

$tempRoot = Join-Path $env:TEMP ("subflow-launcher-test-{0}" -f [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $tempRoot 'fake-bin'
$fakeInstaller = Join-Path $tempRoot 'fake-dotnet-install.ps1'

try {
    New-Item -ItemType Directory -Force $tempRoot, $fakeBin, (Join-Path $tempRoot '.git') | Out-Null
    Copy-Item $launcher (Join-Path $tempRoot 'Run-SubFlow.bat') -Force

    @'
@echo off
if "%1 %2"=="branch --show-current" (
  echo feature/offline-mt-gpu-profiles
  exit /b 0
)
if "%1 %2"=="status --porcelain" exit /b 0
exit /b 0
'@ | Set-Content -Path (Join-Path $fakeBin 'git.cmd') -Encoding Ascii

    @'
@echo off
if "%1"=="--version" (
  echo 8.0.999
  exit /b 0
)
exit /b 0
'@ | Set-Content -Path (Join-Path $fakeBin 'dotnet.cmd') -Encoding Ascii

    @'
param(
    [string]$Channel,
    [string]$InstallDir,
    [switch]$NoPath
)

$ErrorActionPreference = 'Stop'
if ($Channel -ne '10.0') { throw "Expected channel 10.0, got '$Channel'." }
New-Item -ItemType Directory -Force $InstallDir | Out-Null
$source = @"
using System;
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            Console.WriteLine("10.0.999");
            return 0;
        }
        return 0;
    }
}
"@
Add-Type -TypeDefinition $source -OutputAssembly (Join-Path $InstallDir 'dotnet.exe') -OutputType ConsoleApplication
'@ | Set-Content -Path $fakeInstaller -Encoding UTF8

    $oldPath = $env:PATH
    $oldOverride = $env:SUBFLOW_DOTNET_INSTALL_SCRIPT
    try {
        $env:PATH = "$fakeBin;$oldPath"
        $env:SUBFLOW_DOTNET_INSTALL_SCRIPT = $fakeInstaller
        Push-Location $tempRoot
        try {
            & cmd.exe /d /c 'Run-SubFlow.bat --check'
            if ($LASTEXITCODE -ne 0) {
                throw "Launcher did not recover from a system .NET 8 SDK. Exit code: $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $env:PATH = $oldPath
        $env:SUBFLOW_DOTNET_INSTALL_SCRIPT = $oldOverride
    }

    $localDotnet = Join-Path $tempRoot '.run\dotnet\dotnet.exe'
    if (-not (Test-Path $localDotnet -PathType Leaf)) {
        throw "Launcher did not bootstrap a local .NET 10 SDK at $localDotnet"
    }

    $version = & $localDotnet --version
    if ($LASTEXITCODE -ne 0 -or $version.Trim() -notlike '10.*') {
        throw "Bootstrapped local SDK is not .NET 10: '$version'"
    }

    Write-Host 'RUN_SUBFLOW_LAUNCHER_BOOTSTRAP_OK'
}
finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
