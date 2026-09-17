@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AMD-GPU-Runtime.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" echo Instalacja runtime AMD zakonczyla sie bledem %ERR%.
pause
exit /b %ERR%
