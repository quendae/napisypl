@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "EXPECTED_BRANCH=feature/offline-mt-gpu-profiles"
set "CURRENT_BRANCH="
set "CURRENT_COMMIT="
set "DOTNET_MAJOR="
set "DIRTY="

echo.
echo ========================================
echo   SubFlow - update, build and run
echo ========================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Git was not found in PATH.
    echo Install Git for Windows and try again.
    goto :fail
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found in PATH.
    echo Install .NET 10 SDK and try again.
    goto :fail
)

if not exist ".git" (
    echo [ERROR] This script must be run from a cloned SubFlow repository.
    echo.
    echo First-time setup:
    echo   git clone --branch %EXPECTED_BRANCH% --single-branch https://github.com/quendae/napisypl.git
    echo   cd napisypl
    echo   Run-SubFlow.bat
    goto :fail
)

for /f "delims=" %%B in ('git branch --show-current 2^>nul') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [ERROR] Could not determine the current Git branch.
    goto :fail
)

if /i not "%CURRENT_BRANCH%"=="%EXPECTED_BRANCH%" (
    echo [ERROR] Wrong branch: %CURRENT_BRANCH%
    echo Expected: %EXPECTED_BRANCH%
    echo The script will not switch branches automatically.
    goto :fail
)

for /f "delims=" %%S in ('git status --porcelain 2^>nul') do set "DIRTY=1"
if defined DIRTY (
    echo [ERROR] Local changes detected. Nothing was pulled or overwritten.
    echo.
    git status --short
    echo.
    echo Commit, stash, or remove the local changes, then run this file again.
    goto :fail
)

for /f "tokens=1 delims=." %%V in ('dotnet --version 2^>nul') do set "DOTNET_MAJOR=%%V"
if not defined DOTNET_MAJOR (
    echo [ERROR] Could not determine the .NET SDK version.
    goto :fail
)

if %DOTNET_MAJOR% LSS 10 (
    echo [ERROR] .NET 10 SDK or newer is required. Installed major version: %DOTNET_MAJOR%
    goto :fail
)

if /i "%~1"=="--check" (
    echo [CHECK] Git, branch, clean working tree and .NET SDK are OK.
    exit /b 0
)

echo [1/4] Updating %EXPECTED_BRANCH%...
git pull --ff-only
if errorlevel 1 (
    echo [ERROR] git pull --ff-only failed.
    goto :fail
)

for /f "delims=" %%C in ('git rev-parse --short HEAD 2^>nul') do set "CURRENT_COMMIT=%%C"
echo [2/4] Current commit: %CURRENT_COMMIT%

echo [3/4] Restoring dependencies...
dotnet restore NapisyPL.sln
if errorlevel 1 (
    echo [ERROR] dotnet restore failed.
    goto :fail
)

echo [4/4] Starting SubFlow (Release)...
echo.
dotnet run --project src\NapisyPL\NapisyPL.csproj -c Release
if errorlevel 1 (
    echo.
    echo [ERROR] SubFlow exited with an error.
    goto :fail
)

exit /b 0

:fail
echo.
echo SubFlow was not started.
echo Press any key to close this window.
pause >nul
exit /b 1
