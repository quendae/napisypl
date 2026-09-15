@echo off
setlocal EnableExtensions

cd /d "%~dp0"
set "RUN_DIR=%CD%\.run"
set "AMD_RUNTIME_DIR=%RUN_DIR%\nllb-amd-runtime"
set "LOCAL_DOTNET_DIR=%RUN_DIR%\dotnet"
set "LOCAL_DOTNET=%LOCAL_DOTNET_DIR%\dotnet.exe"
set "DOTNET_INSTALL_URL=https://dot.net/v1/dotnet-install.ps1"
set "CURRENT_BRANCH="
set "CURRENT_COMMIT="
set "DOTNET_EXE="
set "DOTNET_MAJOR="
set "DIRTY="
set "FETCH_REFSPEC="

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

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Windows PowerShell was not found.
    goto :fail
)

if not exist ".git" (
    echo [ERROR] This script must be run from a cloned SubFlow repository.
    echo.
    echo First-time setup:
    echo   git clone https://github.com/quendae/napisypl.git
    echo   cd napisypl
    echo   Run-SubFlow.bat
    goto :fail
)

for /f "delims=" %%B in ('git branch --show-current 2^>nul') do set "CURRENT_BRANCH=%%B"
if not defined CURRENT_BRANCH (
    echo [ERROR] Could not determine the current Git branch.
    goto :fail
)

for /f "delims=" %%F in ('git config --local --get-all remote.origin.fetch 2^>nul') do set "FETCH_REFSPEC=%%F"
if /i "%FETCH_REFSPEC%"=="+refs/heads/feature/offline-mt-gpu-profiles:refs/remotes/origin/feature/offline-mt-gpu-profiles" (
    echo [GIT] Updating legacy single-branch fetch configuration...
    git config --local remote.origin.fetch "+refs/heads/*:refs/remotes/origin/*"
    if errorlevel 1 (
        echo [ERROR] Could not update the Git fetch configuration.
        goto :fail
    )
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

call :try_dotnet "%LOCAL_DOTNET%"
if not defined DOTNET_EXE (
    where dotnet >nul 2>nul
    if not errorlevel 1 call :try_dotnet dotnet
)

if not defined DOTNET_EXE (
    echo [SETUP] .NET 10 SDK was not found. Installing a private copy for SubFlow...
    call :install_dotnet
    if errorlevel 1 goto :fail
    call :try_dotnet "%LOCAL_DOTNET%"
)

if not defined DOTNET_EXE (
    echo [ERROR] .NET 10 SDK bootstrap completed, but a usable SDK was not found.
    goto :fail
)

if /i "%DOTNET_EXE%"=="%LOCAL_DOTNET%" (
    set "DOTNET_ROOT=%LOCAL_DOTNET_DIR%"
    set "DOTNET_ROOT_X64=%LOCAL_DOTNET_DIR%"
)

echo [SDK] Using .NET %DOTNET_MAJOR% via %DOTNET_EXE%

if /i "%~1"=="--check" (
    echo [CHECK] Git, current branch, clean working tree, PowerShell and .NET SDK are OK.
    exit /b 0
)

echo [1/6] Updating %CURRENT_BRANCH%...
git pull --ff-only
if errorlevel 1 (
    echo [ERROR] git pull --ff-only failed.
    goto :fail
)

for /f "delims=" %%C in ('git rev-parse --short HEAD 2^>nul') do set "CURRENT_COMMIT=%%C"
echo [2/6] Current commit: %CURRENT_COMMIT%

echo [3/6] Restoring dependencies...
"%DOTNET_EXE%" restore NapisyPL.sln
if errorlevel 1 (
    echo [ERROR] dotnet restore failed.
    goto :fail
)

echo [4/6] Publishing SubFlow to .run...
"%DOTNET_EXE%" publish src\NapisyPL\NapisyPL.csproj -c Release -o "%RUN_DIR%"
if errorlevel 1 (
    echo [ERROR] dotnet publish failed.
    goto :fail
)

if not exist "%RUN_DIR%\NapisyPL.exe" (
    echo [ERROR] Published SubFlow executable was not found.
    goto :fail
)

echo [5/6] Checking AMD offline-MT runtime...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%CD%\tools\offline-mt\install-amd-runtime.ps1" -Destination "%AMD_RUNTIME_DIR%"
if errorlevel 1 (
    echo [ERROR] AMD offline-MT runtime setup failed.
    goto :fail
)

copy /Y "%CD%\tools\offline-mt\nllb_helper.py" "%AMD_RUNTIME_DIR%\nllb_helper.py" >nul
if errorlevel 1 (
    echo [ERROR] Could not update the offline-MT helper.
    goto :fail
)

echo [6/6] Starting SubFlow...
echo.
"%RUN_DIR%\NapisyPL.exe"
if errorlevel 1 (
    echo.
    echo [ERROR] SubFlow exited with an error.
    goto :fail
)

exit /b 0

:try_dotnet
set "DOTNET_CANDIDATE=%~1"
set "DOTNET_CANDIDATE_MAJOR="
for /f "tokens=1 delims=." %%V in ('"%DOTNET_CANDIDATE%" --version 2^>nul') do set "DOTNET_CANDIDATE_MAJOR=%%V"
if not defined DOTNET_CANDIDATE_MAJOR exit /b 0
if %DOTNET_CANDIDATE_MAJOR% LSS 10 exit /b 0
set "DOTNET_EXE=%DOTNET_CANDIDATE%"
set "DOTNET_MAJOR=%DOTNET_CANDIDATE_MAJOR%"
exit /b 0

:install_dotnet
if exist "%LOCAL_DOTNET_DIR%" rmdir /s /q "%LOCAL_DOTNET_DIR%"
mkdir "%LOCAL_DOTNET_DIR%" >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Could not create %LOCAL_DOTNET_DIR%.
    exit /b 1
)

set "DOTNET_INSTALL_SCRIPT=%SUBFLOW_DOTNET_INSTALL_SCRIPT%"
if not defined DOTNET_INSTALL_SCRIPT (
    set "DOTNET_INSTALL_SCRIPT=%RUN_DIR%\dotnet-install.ps1"
    set "SUBFLOW_DOTNET_INSTALL_DOWNLOAD=%RUN_DIR%\dotnet-install.ps1"
    echo [SETUP] Downloading Microsoft's .NET installer...
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '%DOTNET_INSTALL_URL%' -OutFile $env:SUBFLOW_DOTNET_INSTALL_DOWNLOAD"
    if errorlevel 1 (
        echo [ERROR] Could not download the .NET installer.
        exit /b 1
    )
)

if not exist "%DOTNET_INSTALL_SCRIPT%" (
    echo [ERROR] .NET installer script was not found: %DOTNET_INSTALL_SCRIPT%
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%DOTNET_INSTALL_SCRIPT%" -Channel 10.0 -InstallDir "%LOCAL_DOTNET_DIR%" -NoPath
if errorlevel 1 (
    echo [ERROR] Local .NET 10 SDK installation failed.
    exit /b 1
)

if not exist "%LOCAL_DOTNET%" (
    echo [ERROR] Local .NET installer did not create %LOCAL_DOTNET%.
    exit /b 1
)

exit /b 0

:fail
echo.
echo SubFlow was not started.
echo Press any key to close this window.
pause >nul
exit /b 1
