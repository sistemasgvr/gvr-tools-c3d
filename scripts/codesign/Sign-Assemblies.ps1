#requires -version 5.1
<#
.SYNOPSIS
  Firma Authenticode (SHA256) sobre DLLs/EXE de GVR Tools.

.DESCRIPTION
  Orden de certificado:
    1. GVR_CODESIGN_PFX + GVR_CODESIGN_PASSWORD (producción / CA comercial)
    2. certs/gvr-dev-codesign.pfx (desarrollo; crear con Ensure-DevCertificate.ps1)

  Por defecto firma GvrTools*.dll bajo -Path. Usa timestamp DigiCert si hay red.

.EXAMPLE
  .\scripts\codesign\Sign-Assemblies.ps1 -Path build\2021

.EXAMPLE
  .\scripts\codesign\Sign-Assemblies.ps1 -Path dist\GvrTools-Setup-1.0.0.exe
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$PfxPath,
    [string]$Password,
    [switch]$SkipTimestamp
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

function Find-SignTool {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    $found = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\\x64$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    throw "No se encontró signtool.exe. Instala Windows SDK / Visual Studio Build Tools."
}

if (-not $PfxPath) { $PfxPath = $env:GVR_CODESIGN_PFX }
if (-not $Password) { $Password = $env:GVR_CODESIGN_PASSWORD }

if (-not $PfxPath) {
    $devPfx = Join-Path $repoRoot "certs\gvr-dev-codesign.pfx"
    $devPass = Join-Path $repoRoot "certs\gvr-dev-codesign.password.txt"
    if (Test-Path $devPfx) {
        $PfxPath = $devPfx
        if (-not $Password -and (Test-Path $devPass)) {
            $Password = (Get-Content $devPass -Raw).Trim()
        }
    }
}

if (-not $PfxPath -or -not (Test-Path $PfxPath)) {
    throw "No hay certificado. Ejecuta .\scripts\codesign\Ensure-DevCertificate.ps1 o define GVR_CODESIGN_PFX."
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Falta la contraseña del PFX (GVR_CODESIGN_PASSWORD o certs/gvr-dev-codesign.password.txt)."
}

$files = New-Object System.Collections.Generic.List[string]
foreach ($entry in $Path) {
    $resolved = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $repoRoot $entry }
    if (-not (Test-Path $resolved)) {
        throw "No existe: $resolved"
    }
    $item = Get-Item $resolved
    if ($item.PSIsContainer) {
        Get-ChildItem $item.FullName -Filter "GvrTools*.dll" -File | ForEach-Object { $files.Add($_.FullName) }
        Get-ChildItem $item.FullName -Filter "GvrTools*.exe" -File -ErrorAction SilentlyContinue | ForEach-Object { $files.Add($_.FullName) }
    }
    else {
        $files.Add($item.FullName)
    }
}

if ($files.Count -eq 0) {
    throw "No hay archivos GvrTools*.dll/.exe que firmar bajo: $($Path -join ', ')"
}

$signtool = Find-SignTool
$timestampArgs = @()
if (-not $SkipTimestamp) {
    $timestampArgs = @("/tr", "http://timestamp.digicert.com", "/td", "SHA256")
}

foreach ($file in $files) {
    Write-Host "Firmando: $file" -ForegroundColor Cyan
    $args = @("sign", "/fd", "SHA256", "/f", $PfxPath, "/p", $Password) + $timestampArgs + @($file)
    & $signtool @args
    if ($LASTEXITCODE -ne 0 -and -not $SkipTimestamp) {
        Write-Host "Timestamp falló; reintentando sin timestamp..." -ForegroundColor Yellow
        & $signtool sign /fd SHA256 /f $PfxPath /p $Password $file
        if ($LASTEXITCODE -ne 0) {
            throw "signtool falló para $file"
        }
    }
    elseif ($LASTEXITCODE -ne 0) {
        throw "signtool falló para $file"
    }
}

Write-Host "Firmados $($files.Count) archivo(s)." -ForegroundColor Green
