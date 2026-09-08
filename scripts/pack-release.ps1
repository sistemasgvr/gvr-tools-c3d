#requires -version 5.1
<#
.SYNOPSIS
    Empaqueta GVR Tools para distribución a usuarios finales (ZIP + instalador .exe).

.DESCRIPTION
    1. Compila todas las versiones Civil 3D soportadas (o usa build\ ya existente con -SkipBuild).
    2. Compila tools\GvrTools.Installer (Instalar-GvrTools.exe, net48 — no requiere SDK en el PC del usuario).
    3. Arma dist\GvrTools-C3D-<version>\ con:
         Instalar-GvrTools.exe
         Desinstalar.bat
         LEEME.txt
         GvrTools.bundle\PackageContents.xml
         GvrTools.bundle\Contents\<año>\*.dll
    4. Genera dist\GvrTools-C3D-<version>.zip listo para enviar.

    El usuario final solo descomprime y hace doble clic en Instalar-GvrTools.exe.
    Civil 3D elige solo la carpeta Contents\<suAño>\ según PackageContents.xml (Series R24/R25/R26).

.PARAMETER SkipBuild
    No recompila los plugins; usa lo que haya en build\<año>\.

.PARAMETER Configuration
    Configuración de compilación. Por defecto Release.
#>
param(
    [switch]$SkipBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$SupportedVersions = @(2021, 2022, 2023, 2024, 2025, 2026, 2027)
$BundleFolderName = "GvrTools.bundle"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\GvrTools.App\GvrTools.App.csproj"
$installerProject = Join-Path $repoRoot "tools\GvrTools.Installer\GvrTools.Installer.csproj"
$sourceManifest = Join-Path $repoRoot "deploy\$BundleFolderName\PackageContents.xml"
$buildRoot = Join-Path $repoRoot "build"
$distRoot = Join-Path $repoRoot "dist"

# Read product version from Directory.Build.props when possible
$productVersion = "1.0.0"
$propsFile = Join-Path $repoRoot "src\Directory.Build.props"
if (Test-Path $propsFile) {
    $m = Select-String -Path $propsFile -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if ($m) { $productVersion = $m.Matches[0].Groups[1].Value }
}

$releaseName = "GvrTools-C3D-$productVersion"
$stageDir = Join-Path $distRoot $releaseName
$zipPath = Join-Path $distRoot "$releaseName.zip"

function Find-DotNet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
    if (Test-Path $fallback) { return $fallback }
    throw "No se encontró 'dotnet'. Instala el .NET SDK para empaquetar (el usuario final NO lo necesita)."
}

$dotnet = Find-DotNet

if (-not (Test-Path $sourceManifest)) {
    throw "Falta el manifiesto: $sourceManifest"
}

if (-not $SkipBuild) {
    foreach ($year in $SupportedVersions) {
        Write-Host "Compilando plugin C3D $year..." -ForegroundColor Cyan
        & $dotnet build $appProject -c $Configuration -p:C3DVersion=$year --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Falló la compilación para Civil 3D $year."
        }
    }
}

$availableYears = @()
foreach ($year in $SupportedVersions) {
    $dll = Join-Path $buildRoot "$year\GvrTools.App.dll"
    if (Test-Path $dll) {
        $availableYears += $year
    } else {
        Write-Host "Aviso: no hay build\$year (se omite en el paquete)." -ForegroundColor Yellow
    }
}

if ($availableYears.Count -eq 0) {
    throw "No hay ninguna carpeta build\<año>\ con GvrTools.App.dll. Compila primero o quita -SkipBuild."
}

Write-Host "Compilando instalador (Instalar-GvrTools.exe)..." -ForegroundColor Cyan
& $dotnet build $installerProject -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación del instalador."
}

$installerExe = Join-Path $repoRoot "dist\installer-build\Instalar-GvrTools.exe"
if (-not (Test-Path $installerExe)) {
    throw "No se generó $installerExe"
}

if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

$bundleStage = Join-Path $stageDir $BundleFolderName
New-Item -ItemType Directory -Path $bundleStage -Force | Out-Null
Copy-Item $sourceManifest (Join-Path $bundleStage "PackageContents.xml") -Force

foreach ($year in $availableYears) {
    $dest = Join-Path $bundleStage "Contents\$year"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item (Join-Path $buildRoot "$year\*") $dest -Recurse -Force
    Write-Host "  + Contents\$year" -ForegroundColor Green
}

Copy-Item $installerExe (Join-Path $stageDir "Instalar-GvrTools.exe") -Force

@"
@echo off
"%~dp0Instalar-GvrTools.exe" /uninstall
pause
"@ | Set-Content -Path (Join-Path $stageDir "Desinstalar.bat") -Encoding ASCII

@"
GVR Tools para AutoCAD Civil 3D
================================

Instalacion (usuario final — NO necesita Visual Studio ni .NET SDK)
-------------------------------------------------------------------
1. Descomprime este ZIP en cualquier carpeta.
2. Ejecuta Instalar-GvrTools.exe (doble clic).
3. Reinicia Civil 3D.
4. Busca la pestana "GVR Tools" o escribe el comando: GVRBATCHEXPORT

Que hace el instalador
----------------------
- Detecta las versiones de Civil 3D instaladas en el PC (informativo).
- Copia GvrTools.bundle a:
    %APPDATA%\Autodesk\ApplicationPlugins\GvrTools.bundle\

Como elige Civil 3D su version
------------------------------
El archivo PackageContents.xml declara una carpeta Contents\<ano>\ por cada release
(2021…2027). Al arrancar, Civil 3D carga SOLO la carpeta que coincide con su serie
(R24.0=2021 … R26.0=2027). No hace falta elegir version a mano.

Versiones incluidas en este paquete
-----------------------------------
$($availableYears -join ', ')

Desinstalacion
--------------
Ejecuta Desinstalar.bat o: Instalar-GvrTools.exe /uninstall
"@ | Set-Content -Path (Join-Path $stageDir "LEEME.txt") -Encoding UTF8

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Write-Host "Creando ZIP..." -ForegroundColor Cyan
Compress-Archive -Path $stageDir -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Paquete listo para distribuir:" -ForegroundColor Green
Write-Host "  Carpeta: $stageDir"
Write-Host "  ZIP:     $zipPath"
Write-Host "  Anos:    $($availableYears -join ', ')"
Write-Host ""
Write-Host "Envia el ZIP al usuario. Solo necesita Instalar-GvrTools.exe." -ForegroundColor Yellow
