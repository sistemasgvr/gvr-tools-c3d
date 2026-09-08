@echo off
setlocal

set "SCRIPT_DIR=%~dp0"

echo ===============================================
echo   GVR Tools - Instalador DE DESARROLLO
echo ===============================================
echo.
echo Requiere .NET SDK. Compila e instala en este PC.
echo Usuarios finales: usa dist\GvrTools-C3D-*.zip
echo   o Instalar-GvrTools.bat (sin SDK).
echo.
set /p C3DVER="Version unica (Enter = multi-version / autodetectar): "

if "%C3DVER%"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\install-plugin.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%scripts\install-plugin.ps1" -C3DVersion %C3DVER%
)

echo.
pause
