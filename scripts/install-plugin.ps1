#requires -version 5.1
<#
.SYNOPSIS
    Compila (si hay SDK) e instala GVR Tools como bundle de Civil 3D en este equipo.

.DESCRIPTION
    Sin .NET SDK, usa automaticamente -SkipBuild si existen DLLs en build\<ano>\.
    Para usuarios finales sin codigo fuente, preferir dist\GvrTools-C3D-*.zip
    (Instalar-GvrTools.exe).

.PARAMETER C3DVersion
    Una sola version. Si se omite, instala todas las builds disponibles (multi-version).

.PARAMETER SkipBuild
    No compila; solo copia build\<ano>\.

.PARAMETER Uninstall
    Quita el bundle.
#>
param(
    [int]$C3DVersion,
    [switch]$AllVersions,
    [string]$Configuration = "Release",
    [switch]$AllUsers,
    [switch]$SkipBuild,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

$SupportedVersions = @(2021, 2022, 2023, 2024, 2025, 2026, 2027)
$BundleFolderName = "GvrTools.bundle"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\GvrTools.App\GvrTools.App.csproj"
$sourceBundle = Join-Path $repoRoot "deploy\$BundleFolderName"
$buildRoot = Join-Path $repoRoot "build"

function Get-InstalledCivil3DVersions {
    $found = @()
    foreach ($version in $SupportedVersions) {
        $acadDir = Join-Path ${env:ProgramFiles} "Autodesk\AutoCAD $version"
        if ((Test-Path (Join-Path $acadDir "acad.exe")) -and (Test-Path (Join-Path $acadDir "C3D"))) {
            $found += $version
        }
    }
    return $found
}

function Get-ApplicationPluginsDirectory {
    if ($AllUsers) { return Join-Path $env:ProgramData "Autodesk\ApplicationPlugins" }
    return Join-Path $env:APPDATA "Autodesk\ApplicationPlugins"
}

function Test-DotNetSdk {
    $dotnet = $null
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
    elseif (Test-Path (Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe")) {
        $dotnet = Join-Path ${env:ProgramFiles} "dotnet\dotnet.exe"
    }
    if (-not $dotnet) { return $null }

    # Runtime-only installs have "dotnet" but no SDKs — that is what failed on the user PC.
    $sdks = & $dotnet --list-sdks 2>$null
    if (-not $sdks) { return $null }
    return $dotnet
}

function Get-AvailableBuildYears {
    param([int[]]$Candidates)
    $list = @()
    foreach ($year in $Candidates) {
        if (Test-Path (Join-Path $buildRoot "$year\GvrTools.App.dll")) { $list += $year }
    }
    return $list
}

$targetDir = Join-Path (Get-ApplicationPluginsDirectory) $BundleFolderName

if ($Uninstall) {
    if (Test-Path $targetDir) {
        Remove-Item $targetDir -Recurse -Force
        Write-Host "Quitado: $targetDir" -ForegroundColor Yellow
    }
    Write-Host "Desinstalacion completa. Reinicia Civil 3D." -ForegroundColor Green
    return
}

if (-not (Test-Path (Join-Path $sourceBundle "PackageContents.xml"))) {
    throw "Falta deploy\$BundleFolderName\PackageContents.xml"
}

$useMulti = $AllVersions -or (-not $PSBoundParameters.ContainsKey('C3DVersion')) -or ($C3DVersion -eq 0)
if ($PSBoundParameters.ContainsKey('C3DVersion') -and $C3DVersion -gt 0) {
    $useMulti = $false
}

if (-not $useMulti) {
    if ($SupportedVersions -notcontains $C3DVersion) {
        throw "Version no soportada: $C3DVersion. Soportadas: $($SupportedVersions -join ', ')."
    }
    $yearsToInstall = @($C3DVersion)
} else {
    $detected = Get-InstalledCivil3DVersions
    if ($detected.Count -gt 0) {
        Write-Host "Civil 3D detectado: $($detected -join ', ')" -ForegroundColor Cyan
    }
    $yearsToInstall = @($SupportedVersions)
    Write-Host "Instalacion multi-version (Contents\<ano>\ por cada build disponible)." -ForegroundColor Cyan
}

$dotnet = Test-DotNetSdk
if (-not $SkipBuild) {
    if (-not $dotnet) {
        $existing = Get-AvailableBuildYears -Candidates $yearsToInstall
        if ($existing.Count -gt 0) {
            Write-Host "No hay .NET SDK. Se usaran DLLs precompiladas en build\ (sin compilar)." -ForegroundColor Yellow
            $SkipBuild = $true
        } else {
            throw @"
No hay .NET SDK en este PC y tampoco hay DLLs en build\<ano>\.

Usuario final: descomprime dist\GvrTools-C3D-1.0.0.zip y ejecuta Instalar-GvrTools.exe
(o pide ese ZIP a quien desarrolla el complemento).

Desarrollador: instala el SDK desde https://aka.ms/dotnet/download
y luego ejecuta Instalar-Dev.bat o: .\scripts\pack-release.ps1
"@
        }
    }
}

if (-not $SkipBuild) {
    if (-not (Test-Path $project)) {
        throw "No se encontro el proyecto en '$project'."
    }
    foreach ($year in $yearsToInstall) {
        Write-Host "Compilando ($Configuration, C3DVersion=$year)..."
        & $dotnet build $project -c $Configuration -p:C3DVersion=$year --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "La compilacion para Civil 3D $year fallo (codigo $LASTEXITCODE)."
        }
    }
}

$available = Get-AvailableBuildYears -Candidates $yearsToInstall
foreach ($year in $yearsToInstall) {
    if ($available -notcontains $year) {
        Write-Host "Aviso: falta build\$year - se omite." -ForegroundColor Yellow
    }
}

if ($available.Count -eq 0) {
    throw "No hay ensamblados en build\<ano>\. Genera el paquete con scripts\pack-release.ps1 en un PC con SDK."
}

if (Test-Path $targetDir) {
    Remove-Item $targetDir -Recurse -Force
}

New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
Copy-Item (Join-Path $sourceBundle "PackageContents.xml") $targetDir -Force

foreach ($year in $available) {
    $dest = Join-Path $targetDir "Contents\$year"
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    Copy-Item (Join-Path $buildRoot "$year\*") $dest -Recurse -Force
}

Write-Host "Instalado." -ForegroundColor Green
Write-Host "  Bundle: $targetDir"
Write-Host "  Versiones: $($available -join ', ')"
Write-Host ""
Write-Host "Listo. Reinicia Civil 3D y busca la pestana 'GVR Tools'." -ForegroundColor Yellow
