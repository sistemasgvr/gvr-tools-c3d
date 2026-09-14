# Notas de API: trazado PDF (AutoCAD / Civil 3D 2021–2027)

Investigación para `PdfExportEngine` y la UI de configuración completa.
Objetivo: implementar contra la API documentada, no contra supuestos del port Revit.

## Fuentes (revisión por versión)

Las páginas DevGuide de Autodesk reutilizan los mismos GUID; el año en la URL es la edición publicada.
Se consultaron (o se verificó la equivalencia del sample) para los años soportados:

| Año | Plot Settings / Page Setups | Plot Scale / device notes |
| --- | --- | --- |
| 2021 | [GUID-56BD3247…](https://help.autodesk.com/cloudhelp/2021/ENU/OARX-DevGuide-Managed/files/GUID-56BD3247-471C-4471-A238-FFDFDC3BD2E4.htm) | [GUID-BDDF7C1B…](https://help.autodesk.com/cloudhelp/2021/ENU/OARX-DevGuide-Managed/files/GUID-BDDF7C1B-797D-4C8E-AF5F-7B830638D024.htm) |
| 2022 | Mismo GUID bajo `/2022/` | Equivalente |
| 2023 | Mismo GUID bajo `/2023/` | Equivalente |
| 2024 | [GUID-56BD3247… (2024)](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-DevGuide-Managed/files/GUID-56BD3247-471C-4471-A238-FFDFDC3BD2E4.htm) — sample idéntico | Equivalente |
| 2025 | Mismo GUID bajo `/2025/` | Equivalente |
| 2026 | [Plot From Model Space](https://help.autodesk.com/cloudhelp/2026/ESP/OARX-DevGuide-Managed/files/GUID-6E0B1F7B-7B0E-4E3E-B1FD-018E0A3673BE.htm) | [Plot Device](https://help.autodesk.com/cloudhelp/2026/ESP/OARX-DevGuide-Managed/files/GUID-96E2EFD0-B16B-4A3A-BA6A-A9277BBB3610.htm) |
| 2027 | Doc 2026/2025 como proxy si aún no hay cloudhelp 2027; mismos tipos en SDK net8 | Equivalente esperado |

Managed Reference (métodos `PlotSettingsValidator`): estable desde al menos 2018 —
`SetCustomPrintScale`, `SetUseStandardScale`, `SetStdScaleType`, `SetPlotRotation`,
`GetCanonicalMediaNameList`, `SetCanonicalMediaName`, `RefreshLists`.

## Matriz de miembros por versión (2021–2027)

| Miembro | 21 | 22 | 23 | 24 | 25 | 26 | 27 | Uso en GVR |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `PlotSettings.CopyFrom(Layout)` | Y | Y | Y | Y | Y | Y | Y | Base del override |
| `SetPlotConfigurationName` + `RefreshLists` | Y | Y | Y | Y | Y | Y | Y | PC3 + listas |
| `GetCanonicalMediaNameList` / `SetCanonicalMediaName` | Y | Y | Y | Y | Y | Y | Y | Papel layout / ISO / SelectFromDevice |
| `GetPlotDeviceList()` | Y | Y | Y | Y | Y | Y | Y | Combo PC3 |
| `GetPlotStyleSheetList` / `SetCurrentStyleSheet` | Y | Y | Y | Y | Y | Y | Y | CTB/STB |
| `SetPlotType(Extents\|Window\|Display\|Layout)` | Y | Y | Y | Y | Y | Y | Y | Área |
| `SetUseStandardScale` + `StdScale1To1` / `ScaleToFit` | Y | Y | Y | Y | Y | Y | Y | Preset / Fit |
| `SetCustomPrintScale(CustomScale)` | Y | Y | Y | Y | Y | Y | Y | Escala personalizada (mm = unidad) |
| `SetPlotCentered` | Y | Y | Y | Y | Y | Y | Y | Centrar |
| `PlotSettings.ScaleLineweights` | Y | Y | Y | Y | Y | Y | Y | Scale lineweights |
| `SetPlotRotation(PlotRotation)` | Y | Y | Y | Y | Y | Y | Y | Orientación Vertical/Horizontal |
| `PrintLineweights` / `PlotTransparency` / `PlotPlotStyles` / `DrawViewportsFirst` | Y | Y | Y | Y | Y | Y | Y | Casillas Plot |
| `PrinterConfigPath` (prefs Files) | Y | Y | Y | Y | Y | Y | Y | Instalar HQ `.pc3` |

**Decisión:** un solo camino de código (sin `#if` por año). El nombre correcto de escala custom es
`SetCustomPrintScale` (no `SetCustomScale`).

## Área de trazado

```csharp
validator.SetPlotType(plotSettings, PlotType.Extents); // default reunión
// Window | Display | Layout — mismo SetPlotType
```

Para `PlotType.Window`: el batch no dibuja ventana; usa la del layout (CopyFrom). Si no es usable → error claro.

## Escala: preset, Fit, custom

```csharp
// Preset GVR
validator.SetUseStandardScale(ps, true);
validator.SetStdScaleType(ps, StdScaleType.StdScale1To1);
validator.SetPlotCentered(ps, true);

// Fit to paper
validator.SetStdScaleType(ps, StdScaleType.ScaleToFit);

// Custom (paper units = drawing units), DevGuide / Managed Ref
validator.SetUseStandardScale(ps, false);
validator.SetCustomPrintScale(ps, new CustomScale(numerator, denominator)); // e.g. 1 mm = 1 unit

ps.ScaleLineweights = false; // casilla “Scale lineweights”
```

## Orientación

```csharp
// Landscape → Degrees000; Portrait → Degrees090 (DevGuide sample SetPlotRotation)
validator.SetPlotRotation(ps, PlotRotation.Degrees000);
```

Al forzar ISO full bleed, la preferencia landscape/portrait también guía
`IsoFullBleedMediaPicker.Pick(..., preferLandscape)`.

## Casillas del diálogo Plot

```csharp
ps.PrintLineweights = true;
ps.PlotTransparency = true;
ps.PlotPlotStyles = true;
ps.DrawViewportsFirst = true; // “Plot paperspace last”
```

## Papel: layout / ISO full bleed / lista del device

Modos UI:

1. **ForceIsoFullBleed** (default) — fuerza A0–A4; default **A4**.
2. **UseLayout** — `CanonicalMediaName` del layout (+ `PlotMediaResolver` fallback).
3. **SelectFromDevice** — media exacto de `GetCanonicalMediaNameList` del PC3 elegido.

Nombres canónicos típicos (`DWG To PDF.pc3`):

- Prefijo: `ISO_full_bleed_A0_` … `ISO_full_bleed_A4_`
- Suele haber dos entradas por tamaño (portrait / landscape).

Listar media en UI (sin plotear):

```csharp
using (var ps = new PlotSettings(false))
{
    validator.SetPlotConfigurationName(ps, deviceName, null);
    validator.RefreshLists(ps);
    StringCollection media = validator.GetCanonicalMediaNameList(ps);
}
```

## PC3 HQ

No se genera DPI por API. Se **copia** un `.pc3` empaquetado (`DWG To PDF_HQ_.pc3`) a `PrinterConfigPath`, luego `PlotDeviceRepository.Refresh()`.

## Checklist manual

1. Defaults = Extents + 1:1 + centro + casillas ON + **forzar A4 ISO full bleed** + PC3 forzado.
2. Modo “Elegir del dispositivo” → lista dinámica del PC3; error si el media no existe.
3. Escala: preset off → Fit / custom mm=unidad / Scale lineweights; orientación Vertical/Horizontal.
4. Área Layout / Display / Window (Window solo si el layout ya tiene ventana).
5. Instalar plotter HQ; CTB genérico.
6. Folder batch: DWG cerrado.
7. `scripts\pack-release.ps1`.
