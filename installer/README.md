# Instalador GVR Tools (Inno Setup)

Wizard comercial alineado con `docs/LICENSING_PLAN.md` (Pieza 7). El script de desarrollo [`scripts/install-addin.ps1`](../scripts/install-addin.ps1) **sigue** siendo el camino interno.

## Requisitos

1. [Inno Setup 6](https://jrsoftware.org/isinfo.php) (incluye `ISCC.exe`).
2. Builds Release por año en `build/2021` … `build/2027`.
3. El instalador de PDF24 Creator en
   `installer/prereqs/pdf24-creator-installer.exe` (incluido en el bundle del Setup).

   Este archivo **no está en git** (pesa ~420 MB). En una máquina nueva, descárgalo del sitio
   **oficial** de PDF24 (enlace directo estable documentado por ellos):

   ```powershell
   .\installer\download-pdf24.ps1
   ```

   Equivalente manual:

   ```powershell
   New-Item -ItemType Directory -Force -Path installer\prereqs | Out-Null
   Invoke-WebRequest `
     -Uri "https://download.pdf24.org/pdf24-creator.exe" `
     -OutFile "installer\prereqs\pdf24-creator-installer.exe"
   ```

   `build-installer.ps1` llama a `download-pdf24.ps1` solo si el archivo aún no existe.
   Tras actualizar PDF24 a una versión nueva, vuelve a probar los flags silenciosos del wizard
   (`/VERYSILENT … /COMPONENTS=pdfPrinter`) en una máquina limpia.

```powershell
foreach ($v in 2021,2022,2023,2024,2025,2026,2027) {
  dotnet build src/GvrTools.App/GvrTools.App.csproj -c Release -p:RevitVersion=$v
}
```

O usa el helper:

```powershell
.\installer\build-installer.ps1
```

## Salida

`dist/GvrTools-Setup-1.0.0.exe` (sin firmar).

## Qué hace el wizard

1. Idioma ES/EN  
2. Aceptación de TOS (`installer/assets/TOS.txt`)  
3. Checkboxes Revit 2021–2027 (premarca los detectados en `Program Files\Autodesk\Revit <año>`)
4. Página de prerrequisitos con columnas **Name / Required / Found / Action**:
   - PDF24 se considera instalado solo si existen **binarios** (`pdf24.exe` / `pdf24-DocTool.exe`)
     **y** la impresora Windows `PDF24` (o `PDF24 Creator` / `PDF24 Toolbox`).
     Claves huérfanas `HKLM\SOFTWARE\PDF24` o carpetas vacías tras desinstalar **no** cuentan.
   - Si PDF24 ya está instalado de verdad, muestra **Already installed** y no vuelve a ejecutarlo.
   - Para Revit 2021, PDF24 es obligatorio y se instala automáticamente.
   - Para Revit 2022+, si falta se ofrece como instalación opcional porque Revit ya incluye PDF nativo.
5. Copia DLLs a `%ProgramData%\GVR\GvrTools\<año>\` y escribe el `.addin` en `%ProgramData%`
   para Revit 2021–2026 o en `%ProgramFiles%\Autodesk\Revit\Addins\2027` para Revit 2027  
6. Tras instalar: abrir Revit → **Cuenta / Licencia** → pegar key  

## Branding

El wizard usa el escudo GVR (mismo logo de las ventanas del add-in) como ícono del `.exe`, panel
lateral de Bienvenida/Fin y logo de esquina en cada página -- ver
[`installer/assets/README.md`](assets/README.md) para los tamaños exactos y cómo regenerarlos si
el logo cambia.

## Prerrequisito PDF24

El ejecutable incluido se lanza con:

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOUPDATE /COMPONENTS=pdfPrinter
```

PDF24 documenta `/SILENT` y `/VERYSILENT` para su instalador EXE basado en Inno
Setup. Se usa `/VERYSILENT` para una instalación desatendida, `/NORESTART` para
evitar reinicios iniciados por el prerrequisito y `/SP-` para omitir el aviso
inicial. Al reemplazar el binario por una versión nueva, conserva exactamente el
nombre `pdf24-creator-installer.exe`, verifica la firma del proveedor y prueba el
setup en una máquina limpia.

## Authenticode

Por ahora el producto se distribuye **sin firma**. Revit mostrará “Fabricante desconocido”
(el cliente puede usar **Cargar siempre**) y SmartScreen puede avisar al abrir el Setup.

Los scripts en `scripts/codesign/` quedan por si más adelante compráis un cert de CA:

```powershell
$env:GVR_CODESIGN_PFX = "C:\secure\gvr-codesign.pfx"
$env:GVR_CODESIGN_PASSWORD = "..."
.\scripts\codesign\Sign-Assemblies.ps1 -Path dist\GvrTools-Setup-1.0.0.exe
```

`install-addin.ps1` y `build-installer.ps1` **no** firman automáticamente.
