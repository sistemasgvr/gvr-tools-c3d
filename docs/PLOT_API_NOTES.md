# Notas de API: trazado PDF (AutoCAD / Civil 3D 2021–2027)

Investigación para `PlotExportEngine` (trazador completo) y la UI de configuración del cuadro Trazar.
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
| `GetLocaleMediaName(ps, int)` | Y | Y | Y | Y | Y | Y | Y | Nombres de papel como el cuadro Trazar |
| `PlotConfigManager.SetCurrentConfig` → `PlotConfig` | Y | Y | Y | Y | Y | Y | Y | Trazador / Lugar / Descripción, extensión, plot-to-file, DPI máx. |
| `PlotConfigManager.Devices` (`DeviceType`) | Y | Y | Y | Y | Y | Y | Y | Impresora de Windows vs `.pc3` |
| `SetPlotPaperUnits` / `SetPlotOrigin` | Y | Y | Y | Y | Y | Y | Y | mm/pulgadas, desfase X/Y |
| `SetPlotWindowArea` | Y | Y | Y | Y | Y | Y | Y | Ventana común |
| `PlotHidden` / `ShadePlotResLevel` / `ShadePlotCustomDpi` | Y | Y | Y | Y | Y | Y | Y | Ocultar objetos EP, calidad sombreado |
| `PlotFactory.CreatePreviewEngine` + `PreviewEndPlotInfo` | Y | Y | Y | Y | Y | Y | Y | Vista preliminar |
| `Editor.StartUserInteraction(Window)` | Y | Y | Y | Y | Y | Y | Y | Designar ventana desde la ventana modeless |
| `ObjectContextManager` `ACDB_ANNOTATIONSCALES` | Y | Y | Y | Y | Y | Y | Y | Lista de escalas del dibujo |

Verificado por reflexión sobre los ensamblados de referencia AutoCAD.NET 24.0 (2021) y 24.3 (2024), y
compilando contra el paquete oficial de **cada** versión: 24.0.0 (2021), 24.1.51000 (2022), 24.2.0 (2023),
24.3.0 (2024), 25.0.1 (2025), 25.1.0 (2026) y 26.0.0 (2027) — 0 errores y 0 advertencias (ningún miembro
usado está marcado obsoleto). Comportamiento en ejecución verificado en Civil 3D 2027 (ver abajo).

## Verificación en ejecución (Civil 3D 2027, accoreconsole)

Prueba sin interfaz que crea presentaciones de tamaño conocido (A1 apaisada, A4 vertical, ANSI A con
rotación 90° como el layout por defecto) y las traza con `PlotExportEngine` / `PlotInfoBuilder` reales:

| Caso | Resultado |
| --- | --- |
| A1 + "Tamaño de cada presentación" 1:1 | Hoja A1 completa |
| A1 + ISO A4 forzado 1:1 | **Cortada** (origen -297,-183 mm: solo entra el centro). Es lo esperado: la UI ahora lo avisa y pide confirmación |
| A1 + ISO A4 + "Escala hasta ajustar" | Hoja completa en A4 |
| ANSI A + Horizontal | Idéntico al trazado con la configuración propia del layout (`MediaBox 612x792`, `/Rotate 270`) |
| A4 vertical + Vertical / "Según cada presentación" | Página vertical correcta |
| Girado 180° | `/Rotate 180` en el PDF |
| Desfase 20,10 mm / 1,0.5 pulg | `PlotOrigin` (20,10) / (25.4,12.7) mm |
| Escala 1:2, ventana común, área Presentación, DWF | Correctos |
| PNG sin el papel del layout | Usa el papel por defecto del dispositivo (Sun Hi-Res 1600x1280), no el primero de la lista (16K) |
| Combinar 2 hojas A1 | 1 PDF de 2 páginas |
| Combinar A1 + A4 | `IsCompatibleDocument = false`: AutoCAD no mezcla papeles en un documento → 2 PDF de 1 página |
| Combinar con la última presentación inválida | El PDF conserva las hojas válidas |

Hallazgos que el motor respeta:

- **Las páginas de un documento multi-hoja se retienen hasta recibir una con `isLastPage = true`**; un documento
  cerrado sin ella se escribe con **0 páginas**. Por eso `PlanCombinedPages` valida todas las presentaciones antes
  de trazar la primera (sabe cuál es la última válida y dónde cambia el papel).
- Cada `PlotInfo` entregado al motor debe vivir hasta `EndDocument`.
- Solo páginas compatibles (`PlotInfo.IsCompatibleDocument`: mismo dispositivo y papel) comparten documento.
  La UI ofrece "Combinar" solo si todas las hojas tendrán el mismo papel.
- El motor anterior tenía los dos primeros problemas: combinar con "tamaño del layout" (papeles mezclados) o con
  la última presentación fallida no funcionaba.

**Decisión:** un solo camino de código (sin `#if` por año). El nombre correcto de escala custom es
`SetCustomPrintScale` (no `SetCustomScale`).

## Trazador completo (cuadro Trazar en lote)

Código: `src/GvrTools.Civil3D/Export/Plotting/` (motor, `PlotInfoBuilder`, vista preliminar,
configuraciones de página) y `PlotDeviceRepository`. UI compartida: `PlotConfigurationViewModel` +
`PlotConfigurationView` (pestaña "Trazado" de las dos ventanas).

- **Dispositivos:** todos los de `GetPlotDeviceList()` salvo "None", en el orden de AutoCAD. Para cada uno,
  `PlotConfig.PlotToFileCapability` decide la salida: `MustPlotToFile` (PDF/DWF/PNG `.pc3`) → archivo con
  `DefaultFileExtension`; `PlotToFileAllowed` (impresoras de Windows) → imprime, o `.plt` si "Trazar en archivo";
  `NoPlotToFile` → imprime. `BeginDocument(..., copies, plotToFile, fileName)` recibe las copias solo al imprimir.
- **Combinar en un archivo:** solo PDF/DWF (multi-hoja), configuración manual (un único dispositivo) y el mismo
  papel en todas las hojas (ver verificación).
- **Flujo por pasos:** 1. Presentaciones → 2. Trazado → 3. Salida (`WizardStepsViewModel`). Una pestaña solo se
  abre con el paso anterior completo; "Trazar" se activa al llegar a Salida. Al abrir no hay presentaciones marcadas.
- **Aviso de recorte:** si el papel forzado es menor que el de las presentaciones a la escala elegida
  (`PlotPaperFit`), el panel lo avisa y "Trazar" pide confirmación.
- **Preferencias antiguas:** las claves `Pdf*` de la primera versión se migran (`LegacyPdfPreferences`) mientras
  no se haya trazado con la nueva.
- **Papel:** combo agrupado "Opciones GVR" (tamaño de cada presentación, ISO full bleed A0–A4 según orientación,
  solo si el dispositivo los tiene) + tamaños del dispositivo con `GetLocaleMediaName`. Al cambiar de dispositivo,
  si el papel no existe se usa el papel por defecto del dispositivo (el que deja `SetPlotConfigurationName`).
- **Escala:** la lista del dibujo (EDITARLISTAESC) o una lista métrica por defecto; 1:1 → `StdScale1To1`,
  el resto → `SetCustomPrintScale`. Unidades mm/pulgadas; los dispositivos ráster usan píxeles (no se tocan).
- **Desfase:** `PlotOrigin` se guarda **siempre en mm** (DXF 46/47); pulgadas × 25.4. Con área "Presentación"
  AutoCAD no permite centrar, igual que el cuadro Trazar.
- **Ventanas sombreadas:** en presentaciones el modo de sombreado es de cada ventana gráfica; solo se aplican
  calidad (`ShadePlotResLevel`) y PPP personalizados (limitados por `MaximumDeviceDotsPerInch`).
- **Configuración de página:** `<Configuración manual>`, `<Propia de cada presentación>` (traza lo que tiene
  cada layout; solo cambia CTB, "en archivo" y copias) o una configuración con nombre del dibujo o importada de
  un DWT/DWG (`Database.ReadDwgFile`, como PSETUPIN). Elegir una rellena el panel (`PageSetupRepository.ToExportSettings`).
- **Vista preliminar:** mismo `PlotInfoBuilder` que el lote → `CreatePreviewEngine(0)`; la ventana GVR se oculta
  mientras AutoCAD muestra la vista.
- **Preferencias:** el panel se guarda como `PlotExportSettings` (claves `batch-export-plot` y
  `folder-batch-export-plot`); las preferencias PDF anteriores no se migran (se parte del preset GVR).

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

`PlotRotation` es relativa al papel **tal como lo define el dispositivo**. "Horizontal" = el borde largo
del papel arriba, así que la rotación depende de si el papel está definido apaisado (841 x 594) o vertical
(8.5 x 11). `PlotOrientationMath` (Core, con tests) hace la cuenta; "girado 180°" suma dos cuartos de vuelta:

```csharp
int turns = PlotOrientationMath.ToQuarterTurns(landscape, upsideDown, PlotInfoBuilder.IsMediaLandscape(ps));
validator.SetPlotRotation(ps, (PlotRotation)turns); // ANSI A + Horizontal → Degrees090 (como el layout por defecto)
```

Antes se usaba Landscape → 0° / Portrait → 90° fijo: correcto con ISO full bleed apaisado, pero giraba el
dibujo en papeles verticales. Al forzar ISO full bleed, la orientación también guía
`IsoFullBleedMediaPicker.Pick(..., preferLandscape)`.

**"Según cada presentación"** (`PlotDrawingOrientation.FromLayout`, valor por defecto): la orientación y el giro
de 180° se leen de la configuración propia de cada layout (rotación contra SU papel, antes de cambiarlo). Es lo
correcto para lotes que mezclan hojas verticales y apaisadas; una orientación global corta unas u otras.

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

1. Defaults (botón "Preset GVR") = DWG To PDF + ISO full bleed A4 + Extensión + 1:1 + centrado + casillas ON.
2. Combo de dispositivos = mismo listado que el cuadro Trazar (impresoras + `.pc3`); Trazador/Lugar/Descripción
   coinciden; papel con los mismos nombres localizados.
3. PDF `.pc3` → `.pdf`; DWF6 → `.dwf`; PublishToWeb PNG → `.png` (unidades en píxeles); impresora de Windows →
   imprime con N copias, o `.plt` con "Trazar en archivo".
4. Orientación: ANSI A Horizontal y Vertical, ISO full bleed A1 Horizontal y Vertical, y "girado 180°";
   comparar con la vista preliminar de Civil 3D.
5. Escala 1:100 desde la lista del dibujo, personalizada en mm y en pulgadas; desfase X/Y sin centrar.
6. Área Ventana: la de cada presentación y la común ("Designar ventana <" en espacio papel).
7. Configuración de página con nombre del dibujo, importada de un DWT y "Propia de cada presentación".
8. Vista preliminar de la presentación de referencia; Esc vuelve a la ventana GVR.
9. Instalar plotter HQ; CTB genérico; combinar en un PDF.
10. Folder batch: DWG cerrados; impresión directa sin crear carpetas.
11. `scripts\pack-release.ps1`.
