# Notas de API: trazado PDF (AutoCAD / Civil 3D 2021–2027)

Investigación previa a forzar preset Extents / 1:1 / centro + PC3 + CTB en
`PdfExportEngine`. Objetivo: implementar contra la API documentada, no contra
supuestos del port Revit.

## Fuentes

| Tema | Fuente |
| --- | --- |
| Secuencia PlotInfo / PlotEngine | [Plot From Model Space (.NET) — AutoCAD 2026 DevGuide](https://help.autodesk.com/cloudhelp/2026/ESP/OARX-DevGuide-Managed/files/GUID-6E0B1F7B-7B0E-4E3E-B1FD-018E0A3673BE.htm) (mismo patrón en guías 2021+) |
| Listar dispositivos | [Plot Device (.NET)](https://help.autodesk.com/cloudhelp/2026/ESP/OARX-DevGuide-Managed/files/GUID-96E2EFD0-B16B-4A3A-BA6A-A9277BBB3610.htm) — `PlotSettingsValidator.GetPlotDeviceList()` |
| Escala de trazado | [Plot Scale (.NET) 2021](https://help.autodesk.com/cloudhelp/2021/ENU/OARX-DevGuide-Managed/files/GUID-BDDF7C1B-797D-4C8E-AF5F-7B830638D024.htm) |
| `StdScaleType` / 1:1 | Managed Ref: `StdScaleType.StdScale1To1`; foros ADN usan `SetStdScaleType(..., StdScale1To1)` |
| Estilos / opciones | Foros ADN: `PlotPlotStyles`, `PlotTransparency` en `PlotSettings` |
| Patrón headless histórico | Kean Walmsley, “Driving a basic AutoCAD plot using .NET” (2007; API estable) |

## Matriz de miembros (estables 2021–2027)

| Miembro | Uso en GVR | net48 (2021–2024) | net8/net10 (2025–2027) |
| --- | --- | --- | --- |
| `PlotSettings.CopyFrom(Layout)` | Base del override | Igual | Igual |
| `PlotSettingsValidator.SetPlotConfigurationName(ps, device, mediaOrNull)` | Forzar PC3 | Igual | Igual |
| `RefreshLists` / `GetCanonicalMediaNameList` / `SetCanonicalMediaName` | Conservar papel del layout | Igual | Igual |
| `GetPlotDeviceList()` | Combo de PC3 | Igual; lista cacheada por sesión → llamar `RefreshLists` tras instalar PC3 | Igual |
| `GetPlotStyleSheetList()` / `SetCurrentStyleSheet` | CTB/STB | Igual | Igual |
| `SetPlotType(Extents)` | Área | Igual | Igual |
| `SetUseStandardScale(true)` + `SetStdScaleType(StdScale1To1)` | Escala 1:1 (no ScaleToFit) | Igual | Igual |
| `SetPlotCentered(true)` | Centrar | Igual | Igual |
| `PlotSettings.PlotPlotStyles = true` | Usar plumas | Igual | Igual |
| `PlotSettings.PlotTransparency` | Checkbox UI | Presente desde hace años en managed API | Igual |
| `PlotEngine` BeginPlot → BeginDocument → BeginPage → graphics → End* | Un PDF o multi-hoja | Igual | Igual |
| `DocumentCollection.Open` / close | Carpeta de DWG | Igual; no abrir si ya está en sesión | Igual |

**Decisión:** un solo camino de código (sin `#if` por año) para preset/PC3/CTB/transparencia. Los paquetes `AutoCAD.NET` por `C3DVersion` ya aportan las referencias correctas; los símbolos `C3D202x_OR_GREATER` no hacen falta para estos miembros.

## Enum de escala 1:1

```csharp
validator.SetUseStandardScale(plotSettings, true);
validator.SetStdScaleType(plotSettings, StdScaleType.StdScale1To1);
```

`ScaleToFit` es exactamente “ajustar a la página” (reunión: desactivado).

## Papel del layout

Tras `SetPlotConfigurationName(device, null)` se pierde o invalida el media. Flujo documentado en el motor:

1. Guardar `CanonicalMediaName` del layout.
2. Asignar dispositivo.
3. `RefreshLists` + si el media guardado está en `GetCanonicalMediaNameList`, reaplicarlo.
4. Solo si no está: fallback a un media común (`PickDefaultMedia`) con warning en log.

## PC3 HQ

No se crea por API. El usuario guarda un `.pc3` modificado (Custom Properties / DPI) en la carpeta de Plotters; aparece en `GetPlotDeviceList()`. GVR solo lo lista y lo fuerza en todas las hojas.

## Checklist manual (post-implementación)

Ver sección 7 del plan *Batch PDF profesional*.

1. Layout con CTB + papel del layout (p. ej. ISO full bleed A1) → PDF con plumas (no colores crudos).
2. Elegir PC3 HQ → CIP/sello legible vs `DWG To PDF.pc3` normal.
3. Varias presentaciones: **mismo** PC3+CTB en todas; papel distinto por layout respetado.
4. Círculo en zona gris → Extents “rompe” (comportamiento esperado; tooltip lo advierte).
5. `GVRFOLDERBATCHEXPORT` con DWG cerrado OK; abierto → skip con mensaje de “debe estar cerrado”.
6. Regenerar paquete con `scripts\pack-release.ps1` al cerrar el trabajo.
