#requires -version 5.1
<#
.SYNOPSIS
  Descarga PDF24 Creator desde el sitio oficial (si falta en installer/prereqs/).

.DESCRIPTION
  PDF24 publica un enlace directo estable documentado en su help center:
  https://download.pdf24.org/pdf24-creator.exe
  (también: https://www.pdf24.org/products/pdf-creator/download/pdf24-creator.exe)

  El archivo no va a git (~420 MB). Este script lo deja listo para Inno Setup.

.PARAMETER Force
  Vuelve a descargar aunque ya exista el .exe local.
#>
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$prereqDir = Join-Path $PSScriptRoot "prereqs"
$outFile = Join-Path $prereqDir "pdf24-creator-installer.exe"
$uri = "https://download.pdf24.org/pdf24-creator.exe"

if ((Test-Path $outFile) -and -not $Force) {
    $sizeMb = [math]::Round((Get-Item $outFile).Length / 1MB, 1)
    Write-Host "PDF24 ya está en $outFile ($sizeMb MB). Usa -Force para re-descargar." -ForegroundColor Green
    exit 0
}

New-Item -ItemType Directory -Force -Path $prereqDir | Out-Null

Write-Host "Descargando PDF24 Creator desde $uri ..." -ForegroundColor Cyan
Write-Host "Destino: $outFile" -ForegroundColor Cyan

$tmp = "$outFile.download"
try {
    # ProgressPreference silencia la barra lenta de IWR en PowerShell 5.1
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest -Uri $uri -OutFile $tmp -UseBasicParsing

    if (-not (Test-Path $tmp) -or (Get-Item $tmp).Length -lt 1MB) {
        throw "La descarga quedó vacía o incompleta."
    }

    Move-Item -LiteralPath $tmp -Destination $outFile -Force
}
catch {
    if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    throw
}

$sizeMb = [math]::Round((Get-Item $outFile).Length / 1MB, 1)
Write-Host "Listo: $outFile ($sizeMb MB)." -ForegroundColor Green
