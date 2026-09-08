# Arquitectura de GVR Tools

Este documento explica **por qué** el código está dividido así, para que una herramienta nueva se
pueda agregar sin tener que leer todo el repositorio.

## Idea central

GVR Tools es un **contenedor de herramientas**, no una herramienta. El complemento que Revit carga
(`GvrTools.App`) no sabe qué herramientas existen: las descubre. Todo lo específico de una
herramienta vive en su propio proyecto.

```
                       Revit
                         │  carga GvrTools.App.dll (.addin)
                         ▼
┌──────────────────────────────────────────────────────────────┐
│ GvrTools.App          cinta + descubrimiento de herramientas │
│   ToolCatalog  ──escanea──►  GvrTools.Tools.*.dll            │
│   RibbonBuilder ──crea───►   un botón por IRevitTool         │
└──────────────────────────────────────────────────────────────┘
                         │ implementan IRevitTool
                         ▼
┌──────────────────────────────────────────────────────────────┐
│ GvrTools.Tools.BatchExport      (una herramienta = 1 proyecto)│
│   BatchExportTool     botón                                   │
│   BatchExportCommand  IExternalCommand                        │
│   ViewModels / Views  ventana WPF                             │
└──────────────────────────────────────────────────────────────┘
        │                        │                     │
        ▼                        ▼                     ▼
┌────────────────┐   ┌────────────────────┐   ┌──────────────────┐
│ GvrTools.Revit │   │ GvrTools.UI        │   │ GvrTools.Core    │
│ API de Revit   │   │ WPF compartido     │   │ sin dependencias │
│ motores export │   │ tema, MVVM, diálog.│   │ nombres, ajustes │
│ scheduler      │   │                    │   │ log, resultados  │
└────────────────┘   └────────────────────┘   └──────────────────┘
```

Las flechas apuntan siempre hacia abajo. Ninguna capa conoce a la de arriba, y ninguna herramienta
conoce a otra.

## Los cinco proyectos

| Proyecto | Depende de | Contiene |
| --- | --- | --- |
| `GvrTools.Core` | nada | Expansión de patrones de nombre, saneado de rutas, nombres únicos, orden natural, persistencia de preferencias, log a archivo, modelos de resultado de lote. **Sin Revit y sin WPF**, por eso se puede testear con `dotnet test`. |
| `GvrTools.UI` | Core | Tema WPF único (`Theme/GvrTheme.xaml`), `ObservableObject`, `RelayCommand`, `ChoiceItem`, diálogos de Windows (carpeta, mensajes, Explorer), dibujo de íconos vectoriales, y **assets de marca compartidos** (`Icons/Escudo_GVR.png` + `BrandIcons`). **Sin Revit.** |
| `GvrTools.Revit` | Core | Todo lo que toca la API de Revit: contrato `IRevitTool`, `RevitJobScheduler`, lectura de láminas, y los motores de exportación. |
| `GvrTools.Tools.*` | Core, Revit, UI | Una herramienta: su botón, su comando, su ventana y sus preferencias. |
| `GvrTools.App` | Core, Revit, UI | El `IExternalApplication`. Construye la cinta con lo que encuentre. |

## Cómo agregar una herramienta nueva

Tres pasos, ningún archivo existente se modifica.

**1. Crear el proyecto** `src/GvrTools.Tools.MiHerramienta/GvrTools.Tools.MiHerramienta.csproj`
(copia el de `BatchExport`; el nombre *tiene* que empezar con `GvrTools.Tools.` porque así se
descubre):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\GvrTools.Revit.props" />
  <PropertyGroup>
    <AssemblyName>GvrTools.Tools.MiHerramienta</AssemblyName>
    <RootNamespace>GvrTools.Tools.MiHerramienta</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GvrTools.Core\GvrTools.Core.csproj" />
    <ProjectReference Include="..\GvrTools.Revit\GvrTools.Revit.csproj" />
    <ProjectReference Include="..\GvrTools.UI\GvrTools.UI.csproj" />
  </ItemGroup>
</Project>
```

**2. Declarar el botón y el comando:**

```csharp
public sealed class MiHerramientaTool : RevitToolBase
{
    public override string Id => "GvrMiHerramienta";      // estable para siempre
    public override string Title => "Mi\nherramienta";
    public override string PanelName => "Modelado";        // el panel se crea solo
    public override Type CommandType => typeof(MiHerramientaCommand);
}

[Transaction(TransactionMode.Manual)]
public class MiHerramientaCommand : IExternalCommand { /* ... */ }
```

**3. Referenciar el proyecto desde `GvrTools.App.csproj`** (solo para que se copie a la carpeta de
salida; el descubrimiento sigue siendo por escaneo) y compilar.

El botón aparece en el panel indicado, ordenado por `SortOrder`. Si la herramienta no puede
funcionar en cierta versión de Revit, devuelve `false` en `IsSupported` y simplemente no se muestra.

## Multi-versión: Revit 2021 a 2025

Un único código fuente, cinco binarios. La propiedad `RevitVersion` es la única perilla:

```bash
dotnet build src/GvrTools.App/GvrTools.App.csproj -c Release -p:RevitVersion=2025
```

`src/Directory.Build.props` la resuelve (también desde configuraciones tipo `Release R27`) y
`src/GvrTools.Revit.props` la traduce a:

- **Framework**: `net48` para 2021–2024, `net8.0-windows` para 2025–2026 y
  `net10.0-windows` para 2027+.
- **Paquete de referencia**: `Nice3point.Revit.Api.*` de la versión correspondiente, con
  `ExcludeAssets="runtime"` para no copiar nunca `RevitAPI.dll` junto al complemento.
- **Símbolos**: `REVIT2024` más `REVIT2021_OR_GREATER` … `REVIT2024_OR_GREATER`. El código usa
  siempre la forma `_OR_GREATER`, así que soportar una versión nueva es una línea en el `.props`.

`Core` y `UI` no reciben esos símbolos: son iguales para todas las versiones y se compilan una sola
vez por framework.

Salida: `build/<año>/`, una carpeta plana lista para el `.addin`.

## Exportación: por qué el equipo no se bloquea

Este es el punto donde el diseño anterior fallaba, y hay dos problemas distintos.

### 1. La API de Revit solo se puede tocar desde su propio hilo

La solución obvia — un `for` sobre las láminas dentro del comando — es exactamente lo que hace que
Revit parezca colgado: no puede repintar, la ventana no se actualiza, Cancelar no responde.

`RevitJobScheduler` (en `GvrTools.Revit/Infrastructure`) resuelve esto con el mecanismo que Revit sí
soporta para trabajos largos: un `ExternalEvent` cuyo handler ejecuta **un solo paso** y regresa; el
siguiente disparo se publica por el `Dispatcher` de WPF en prioridad `Background`.

```
Raise ──► handler: exporta lámina i ──► return
                                        │
        Revit procesa su cola (repinta, atiende clics)
                                        │
        Dispatcher.BeginInvoke ──► Raise ──► handler: exporta lámina i+1 ...
```

Entre dos láminas Revit está completamente libre. La barra de progreso avanza sola, Cancelar surte
efecto en la lámina siguiente, y la ventana es *modeless* (`Show`, no `ShowDialog`), así que Revit
tampoco queda bloqueado por un diálogo modal.

Una herramienta nueva que necesite procesar muchos elementos implementa `IRevitStepJob` y obtiene
todo esto gratis.

### 2. PDF: la API nativa contra la impresora de Windows

| Revit | Cómo se genera el PDF | Ventanas |
| --- | --- | --- |
| 2022–2025 | `Document.Export` + `PDFExportOptions` (API nativa) | ninguna |
| 2021 | `PrintManager` contra una impresora PDF de Windows | ninguna, si la impresora respeta la ruta |

En **2022 y superiores** Revit escribe los archivos él mismo: no hay impresora, no hay diálogo de
"Guardar como", nada toma el foco. Es el camino de `NativePdfExportEngine` y es el que hace que la
exportación sea realmente desatendida.

En **2021** no existe esa API (llegó en 2022), así que hay que plotear con una impresora. Y la lección
que costó dos intentos es esta: **"imprimir a un archivo" no es un solo mecanismo, sino varios**, y
cuál aplica depende del driver. Revit solo conoce el suyo (`PrintToFileName`); un driver que lo ignora
hay que avisarle en su propio idioma.

De ahí `IPdfOutputController`, que abstrae precisamente eso: "dile a esta impresora dónde va el
próximo trabajo".

```csharp
IPdfOutputController
    string Description            // para el log y los mensajes de error
    bool UsesRevitPrintToFile     // ¿escribe Revit el archivo, o el driver?
    void DirectNextJob(string path)
    string DescribeFailure()      // qué decirle al usuario si el archivo no apareció
```

| Implementación | Para | Mecanismo |
| --- | --- | --- |
| `RevitPrintToFileOutput` | PDF24, Bullzip, CutePDF, PDFCreator, doPDF… | `PrintManager.PrintToFileName`; Revit captura la salida al archivo |
| `AdobeDistillerOutput` | Adobe PDF | Escribe la ruta en `HKCU\Software\Adobe\Acrobat Distiller\PrinterJobControl`, con el nombre de valor = ruta del EXE anfitrión. Es el canal documentado de Adobe: se consume por trabajo, así que se escribe antes de cada lámina, y se borra en `Dispose` para no redirigir una impresión manual posterior |

`PdfPrinterCatalog` decide cuál usar clasificando cada impresora (`PdfPrinterKind`) por su puerto y su
driver, leídos del registro. Puntos importantes del diseño:

- La lista de drivers silenciosos conocidos es una **allow-list**, no una heurística. La primera
  versión de este código solo descartaba el puerto `PORTPROMPT:` y dio por buena a Adobe PDF, que
  tiene un puerto propio (`Documents\*.pdf`) y pregunta igual. Suponer que un driver desconocido se
  porta bien es lo que hace que un lote se detenga en un diálogo a mitad de camino; lo desconocido se
  marca como `Unknown` y se avisa en la ventana.
- **`AlwaysPrompts` se rechaza, no se automatiza.** "Microsoft Print to PDF" está en `PORTPROMPT:`:
  Windows abre su diálogo nativo en cada lámina y `SubmitPrint()` se bloquea hasta que alguien
  responda. Responderlo por código obliga a buscar la ventana y simular teclado — apoderarse del
  primer plano y del teclado en cada lámina. Y **ocultar la ventana es peor**: el trabajo se bloquea
  igual, solo que ahora invisiblemente. Así que el motor declina y explica qué instalar.
- Con `AdobeDistillerOutput`, `PrintToFile` va en **false**: el PDF lo produce Distiller, Revit solo
  spoolea. Con `RevitPrintToFileOutput` va en true. Por eso la decisión vive en el controlador
  (`UsesRevitPrintToFile`) y no repartida por el motor.

Agregar soporte para otra impresora terca es una implementación más de `IPdfOutputController` y una
entrada en la clasificación.

### El contrato de los motores

```csharp
IExportEngine
    ExportFormat Format
    string StrategyDescription          // se muestra en la ventana
    IExportSession BeginSession(ExportRequest)   // lanza ExportSetupException

IExportSession : IDisposable
    BatchItemResult Export(SheetSnapshot)        // nunca lanza por una lámina
```

Dos niveles de error, a propósito:

- `ExportSetupException` = no se exportó nada; el `Message` está escrito para el usuario y se muestra
  tal cual.
- `BatchItemResult.Failure` = esta lámina falló, el lote continúa y el detalle aparece en la lista de
  resultados de la ventana (no en un cuadro de diálogo al final).

`BeginSession` / `Dispose` existen porque casi todo formato necesita preparar algo por corrida y
deshacerlo después (seleccionar el driver, restaurar la vista activa).

**Agregar un formato** (DWF, IFC, NWC, imágenes) es: un valor en `ExportFormat`, una clase
`IExportFormatSettings`, un `IExportEngine`, y registrarlo en `ExportEngineCatalog.CreateDefault()`.
La ventana, el nombrado, el progreso, la cancelación y el reporte de errores ya funcionan.

## Nombres de archivo

`Core/Naming` hace el trabajo y no sabe nada de Revit:

- `FileNameBuilder` expande `{Token}` contra un diccionario. Los tokens que quedan vacíos se colapsan
  junto con su separador, así que `{SheetNumber} - {RevisionNumber}` da `A-101`, no `A-101 -`.
- `PathSanitizer` reemplaza lo que Windows no acepta, recorta puntos y espacios finales, limita el
  largo y esquiva nombres reservados (`CON`, `PRN`, …).
- `UniqueNameResolver` agrega `(2)`, `(3)`… y **recuerda lo que ya entregó en esta corrida**, no solo
  lo que existe en disco: algunas APIs de exportación escriben el archivo después de responder, y dos
  láminas con el mismo nombre saneado se sobrescribirían.

La lista de tokens disponibles vive en un solo lugar (`GvrTools.Revit/Export/NamingTokens.cs`) y la
ventana construye su ayuda desde ahí, así que no puede haber un token documentado pero no
implementado.

## Preferencias y log

- `FlatFileSettingsStore` guarda `clave=valor` en `%APPDATA%\GVR\GvrTools\<herramienta>.settings`,
  mapeando por reflexión las propiedades públicas (string, bool, int, double, enum). **No usa JSON a
  propósito**: un complemento comparte proceso con Revit, que ya carga sus propias versiones de las
  librerías de serialización habituales, y llevar otra copia es una fuente clásica de fallos de carga
  de ensamblados. Media docena de preferencias planas no lo justifica.
- `RollingFileLog` escribe un archivo por día en `%LOCALAPPDATA%\GVR\GvrTools\logs\` y conserva los
  últimos 10. Nunca lanza excepciones: como mucho se pierde una línea de log.

Ningún proyecto tiene dependencias NuGet **de runtime**. Los únicos paquetes son los ensamblados de
referencia de la API de Revit, que no se copian a la salida.

## Pruebas

`tests/GvrTools.Core.Tests` corre con `dotnet test` sin Revit instalado, porque `Core` no lo
necesita. Eso es la razón práctica de haber separado esa capa: la lógica que más fácil se rompe en
silencio (nombres, colisiones, ordenamiento, persistencia) es la que se puede verificar en un
segundo.
