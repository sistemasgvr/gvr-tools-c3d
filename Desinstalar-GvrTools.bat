@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

echo ===============================================
echo   GVR Tools para Civil 3D - Desinstalador
echo ===============================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\install-plugin.ps1" -Uninstall

echo.
pause
