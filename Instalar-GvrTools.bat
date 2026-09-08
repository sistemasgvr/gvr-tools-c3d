@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

echo ===============================================
echo   GVR Tools para Civil 3D - Instalador
echo ===============================================
echo.
echo Este instalador NO necesita Visual Studio ni .NET SDK.
echo Copia el complemento ya compilado a ApplicationPlugins.
echo.

REM 1) Prefer the release EXE next to this bat (ZIP descomprimido)
if exist "%ROOT%\Instalar-GvrTools.exe" if exist "%ROOT%\GvrTools.bundle\PackageContents.xml" (
    echo Usando instalador del paquete de release...
    "%ROOT%\Instalar-GvrTools.exe"
    goto :end
)

REM 2) Prefer dist\GvrTools-C3D-*\ from a full repo checkout that was packed
for /d %%D in ("%ROOT%\dist\GvrTools-C3D-*") do (
    if exist "%%~D\Instalar-GvrTools.exe" if exist "%%~D\GvrTools.bundle\PackageContents.xml" (
        echo Usando paquete: %%~D
        "%%~D\Instalar-GvrTools.exe"
        goto :end
    )
)

REM 3) Prebuilt DLLs in build\YYYY\ — copy without compiling
if exist "%ROOT%\build\2024\GvrTools.App.dll" goto :prebuilt
if exist "%ROOT%\build\2025\GvrTools.App.dll" goto :prebuilt
if exist "%ROOT%\build\2023\GvrTools.App.dll" goto :prebuilt
if exist "%ROOT%\build\2021\GvrTools.App.dll" goto :prebuilt
goto :no_prebuilt

:prebuilt
echo Se encontraron DLLs en build\ — instalando sin compilar...
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\install-plugin.ps1" -SkipBuild
goto :end

:no_prebuilt
echo.
echo ERROR: No hay paquete precompilado en esta carpeta.
echo.
echo Opciones:
echo   A^) Usa el ZIP de release: dist\GvrTools-C3D-1.0.0.zip
echo      Descomprimelo y ejecuta Instalar-GvrTools.exe
echo.
echo   B^) En un PC de desarrollo con .NET SDK:
echo      powershell -File scripts\pack-release.ps1
echo      Luego envia el ZIP al usuario final.
echo.
echo   C^) Desarrollo local con SDK: Instalar-Dev.bat
echo.

:end
echo.
pause
