#requires -version 5.1
<#
.SYNOPSIS
  Crea (si falta) un certificado Authenticode de desarrollo "CN=GVR" y lo confía
  en el usuario actual para que Revit deje de mostrar "Fabricante desconocido".

.DESCRIPTION
  Solo para máquinas de desarrollo. Clientes externos necesitan un certificado
  de una CA comercial (DigiCert, Sectigo, etc.) vía GVR_CODESIGN_PFX.

  Genera:
    certs/gvr-dev-codesign.pfx
    certs/gvr-dev-codesign.password.txt
    certs/gvr-dev-codesign.cer

  Importa el certificado en Trusted Publishers (CurrentUser). Para Trusted Root,
  Windows puede mostrar un diálogo de seguridad: hay que pulsar Sí.
#>
param(
    [string]$Publisher = "GVR",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$certsDir = Join-Path $repoRoot "certs"
$pfxPath = Join-Path $certsDir "gvr-dev-codesign.pfx"
$passwordPath = Join-Path $certsDir "gvr-dev-codesign.password.txt"
$cerPath = Join-Path $certsDir "gvr-dev-codesign.cer"
$subject = "CN=$Publisher"

if (-not (Test-Path $certsDir)) {
    New-Item -ItemType Directory -Path $certsDir -Force | Out-Null
}

$existing = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Subject -eq $subject -and
        $_.Issuer -eq $subject -and
        $_.NotAfter -gt (Get-Date)
    } |
    Select-Object -First 1

if ($Force -or -not (Test-Path $pfxPath) -or -not $existing) {
    Write-Host "Creando certificado self-signed $subject ..." -ForegroundColor Cyan

    Get-ChildItem Cert:\CurrentUser\My -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject -or $_.Subject -eq "CN=GVR Development CA" } |
        ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue }

    $code = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $subject `
        -KeyExportPolicy Exportable `
        -HashAlgorithm SHA256 `
        -KeyLength 2048 `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5)

    $passwordPlain = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    $secure = ConvertTo-SecureString -String $passwordPlain -Force -AsPlainText

    Export-PfxCertificate -Cert $code -FilePath $pfxPath -Password $secure | Out-Null
    Set-Content -Path $passwordPath -Value $passwordPlain -Encoding ascii -NoNewline
    Export-Certificate -Cert $code -FilePath $cerPath -Type CERT | Out-Null

    $existing = $code
    Write-Host "PFX: $pfxPath" -ForegroundColor Green
}
else {
    Write-Host "Usando certificado existente: $($existing.Thumbprint)" -ForegroundColor Green
    $passwordPlain = (Get-Content $passwordPath -Raw).Trim()
    $secure = ConvertTo-SecureString -String $passwordPlain -Force -AsPlainText
    if (-not (Test-Path $pfxPath)) {
        Export-PfxCertificate -Cert $existing -FilePath $pfxPath -Password $secure | Out-Null
    }
    if (-not (Test-Path $cerPath)) {
        Export-Certificate -Cert $existing -FilePath $cerPath -Type CERT | Out-Null
    }
}

$passwordPlain = (Get-Content $passwordPath -Raw).Trim()
$pfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
    $pfxPath,
    $passwordPlain,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable)

# Trusted Publishers: suele bastar para que Revit no muestre el diálogo.
$pubStore = New-Object System.Security.Cryptography.X509Certificates.X509Store(
    [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPublisher,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
$pubStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
if (-not ($pubStore.Certificates | Where-Object { $_.Thumbprint -eq $pfx.Thumbprint })) {
    $pubStore.Add($pfx)
    Write-Host "Añadido a Trusted Publishers (CurrentUser)." -ForegroundColor Green
}
$pubStore.Close()

# Trusted Root: Windows puede mostrar un diálogo modal — pulsa Sí.
$rootAlready = Test-Path "Cert:\CurrentUser\Root\$($pfx.Thumbprint)"
if (-not $rootAlready) {
    Write-Host ""
    Write-Host "Windows puede pedir confirmar la raíz de confianza. Pulsa Sí / Yes." -ForegroundColor Yellow
    Write-Host ""
    Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\CurrentUser\Root | Out-Null
    Write-Host "Añadido a Trusted Root (CurrentUser)." -ForegroundColor Green
}

Get-ChildItem Cert:\CurrentUser\Root -ErrorAction SilentlyContinue |
    Where-Object { $_.Subject -eq "CN=GVR Development CA" } |
    ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue }

Write-Host "Listo. Publisher: $subject  Thumbprint: $($pfx.Thumbprint)" -ForegroundColor Yellow
Write-Host "Siguiente: .\scripts\codesign\Sign-Assemblies.ps1 -Path build\2021" -ForegroundColor Yellow
