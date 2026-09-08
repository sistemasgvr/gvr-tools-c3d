#requires -version 5.1
<#
.SYNOPSIS
  Compila GVR Tools para Revit 2021-2027 y genera el Setup con Inno Setup (si ISCC está en PATH).
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\GvrTools.App\GvrTools.App.csproj"
$iss = Join-Path $PSScriptRoot "GvrTools.iss"
$pdf24 = Join-Path $PSScriptRoot "prereqs\pdf24-creator-installer.exe"
$versions = @(2021, 2022, 2023, 2024, 2025, 2026, 2027)

# PDF24 no está en git (~420 MB). Si falta, se descarga del sitio oficial.
if (-not (Test-Path $pdf24)) {
    Write-Host "Falta PDF24 en prereqs/. Descargando desde pdf24.org ..." -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot "download-pdf24.ps1")
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $pdf24)) {
        throw "No se pudo obtener pdf24-creator-installer.exe. Ejecuta: .\installer\download-pdf24.ps1"
    }
}

if (-not $SkipBuild) {
    foreach ($version in $versions) {
        Write-Host "=== Build Revit $version ===" -ForegroundColor Cyan
        dotnet build $project -c $Configuration -p:RevitVersion=$version
        if ($LASTEXITCODE -ne 0) {
            throw "Build falló para Revit $version"
        }
    }
}

foreach ($version in $versions) {
    $dll = Join-Path $repoRoot "build\$version\GvrTools.App.dll"
    if (-not (Test-Path $dll)) {
        throw "Falta $dll - compila antes o quita -SkipBuild"
    }
}

if (-not $IsccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $IsccPath) {
    Write-Host "Inno Setup (ISCC.exe) no encontrado. Builds listos en build/<año>/." -ForegroundColor Yellow
    Write-Host "Instala Inno Setup 6 y vuelve a ejecutar, o abre installer\GvrTools.iss." -ForegroundColor Yellow
    return
}

Write-Host "Compilando instalador con $IsccPath ..." -ForegroundColor Cyan
& $IsccPath $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC falló con código $LASTEXITCODE"
}

Write-Host "Listo. Revisa dist\GvrTools-Setup-*.exe (sin firma Authenticode)." -ForegroundColor Green
