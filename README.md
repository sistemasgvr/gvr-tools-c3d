# GVR Tools RVT

Complementos (add-ins) internos de GVR para Autodesk Revit.

Un solo complemento con una pestaña propia, **GVR Tools**, pensado para ir sumando herramientas: el
código está organizado de modo que agregar una herramienta nueva es crear un proyecto, no modificar
el complemento. Ver [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

**Compatible con Revit 2021, 2022, 2023, 2024, 2025, 2026 y 2027** desde un único código fuente.

## Herramientas incluidas

### Exportación masiva de láminas

Exporta las láminas del proyecto activo a **PDF** o **DWG**, un archivo por lámina, dentro de una
subcarpeta con el nombre del proyecto. Equivalente en alcance a ProSheets / DiRoots PDF Export.

- Lista de láminas con casillas, buscador y — si el proyecto los tiene — filtro por **set de
  láminas** guardado en el proyecto.
- Nombre de archivo fijo: `Número - Nombre` (los mismos que aparecen en el título de la lámina).
- Formato **PDF**, **DWG** o **PDF + DWG** (exporta ambos en una sola pasada con una barra de
  progreso continua).
- Opciones PDF: tamaño de papel por lámina (según su rótulo), ajustar a página o escala real 100%,
  con o sin margen, color / escala de grises / blanco y negro, y calidad ráster.
- Opciones DWG: versión de AutoCAD, combinar vistas, coordenadas compartidas, y opcionalmente
  también un PNG por lámina junto al DWG.
- Cada opción de la ventana tiene un tooltip explicando qué hace, así que no hay que leer manual
  para usar el complemento con confianza.
- **No bloquea el equipo ni Revit**: la ventana no es modal, la exportación avanza lámina por lámina
  devolviéndole el control a Revit entre cada una, y se puede cancelar en cualquier momento.
- Barra de progreso, lista de resultados por lámina en la propia ventana (no un cuadro de diálogo al
  final) y detalle exacto del error cuando alguna falla.
- Recuerda entre sesiones la carpeta, el formato, el patrón de nombre y todas las opciones.
- Independiente del idioma de Revit y de Windows.

## Requisitos

- **Revit 2021 a 2027**, en cualquier idioma. Windows 10/11 de 64 bits.
- Para exportar **PDF en Revit 2022 o superior**: nada más. Revit tiene exportador de PDF propio y el
  complemento lo usa directamente.
- Para exportar **PDF en Revit 2021**: una impresora PDF de Windows a la que se le pueda indicar el
  archivo de destino **sin preguntar**. Sirven **Adobe PDF** (vía Acrobat Distiller) y las que
  respetan la ruta de salida directamente (PDF24 Creator, Bullzip PDF Printer, CutePDF Writer,
  PDFCreator, doPDF). **No sirve "Microsoft Print to PDF"**. Ver
  [PDF en Revit 2021](#pdf-en-revit-2021) más abajo.
- Para exportar **DWG**: nada, en ninguna versión.
- Para compilar: [.NET SDK](https://dotnet.microsoft.com/) 8 o superior (probado con 10) y,
  opcionalmente, Visual Studio 2022 con ".NET desktop development".

## Instalación

```powershell
.\scripts\install-addin.ps1
```

Detecta qué versiones de Revit tienes instaladas, compila una por una y registra el complemento para
cada una. Reinicia Revit y busca la pestaña **GVR Tools**.

Si ya tenías instalada la versión anterior del complemento (`GvrTools.MassPdfExport.addin`), el
script la retira para que no aparezcan botones duplicados.

Parámetros útiles:

```powershell
# Solo una versión
.\scripts\install-addin.ps1 -RevitVersion 2027

# Varias versiones a la vez
.\scripts\install-addin.ps1 -RevitVersion 2021,2027

# Para todos los usuarios del equipo (requiere PowerShell como administrador)
.\scripts\install-addin.ps1 -AllUsers

# Solo reescribir los manifiestos, sin recompilar
.\scripts\install-addin.ps1 -SkipBuild

# Compilar en Debug
.\scripts\install-addin.ps1 -Configuration Debug

# Quitar el complemento
.\scripts\install-addin.ps1 -Uninstall
```

Para una instalación manual, ver los comentarios de [deploy/GvrTools.addin](deploy/GvrTools.addin).

## Compilación

```bash
# Una versión concreta; la salida queda en build/<año>/
dotnet build src/GvrTools.App/GvrTools.App.csproj -c Release -p:RevitVersion=2027

# Pruebas unitarias (no requieren tener Revit instalado)
dotnet test tests/GvrTools.Core.Tests/GvrTools.Core.Tests.csproj
```

En Visual Studio también existen las configuraciones `Debug R21` … `Release R27`, que fijan la
versión sin pasar la propiedad a mano.

## Uso

1. Abre un proyecto en Revit y ve a la pestaña **GVR Tools** → **Exportar láminas**.
2. Selecciona las láminas (casillas, buscador o un set guardado).
3. Elige la carpeta destino con **Examinar…**. Debajo se indica la subcarpeta que se creará.
4. Ajusta el patrón de nombre; la línea inferior muestra cómo quedará el primer archivo.
5. Elige formato y opciones.
6. **Exportar**. La ventana sigue usable, Revit sigue usable, y puedes cancelar cuando quieras.

## PDF en Revit 2021

Revit 2021 no tiene API de exportación a PDF — se agregó en Revit 2022 — así que en esa versión el
complemento plotea con una impresora PDF de Windows.

Y ahí está el problema real: **"imprimir a un archivo" no es un solo mecanismo, sino varios**, y cuál
aplica depende del driver. Revit solo conoce el suyo (`PrintToFileName`); un driver que lo ignora hay
que avisarle en su propio idioma, antes de enviar el trabajo. El complemento clasifica cada impresora
leyendo su puerto y su driver del registro de Windows, y elige el mecanismo correspondiente:

| Tipo | Impresoras | Cómo se le indica el destino |
| --- | --- | --- |
| `WritesToGivenPath` | PDF24, Bullzip, CutePDF, PDFCreator, doPDF, PDF-XChange, Foxit | `PrintManager.PrintToFileName` (Revit escribe el archivo) |
| `AdobeDistiller` | Adobe PDF | Se escribe la ruta de destino en `HKCU\Software\Adobe\Acrobat Distiller\PrinterJobControl` antes de cada lámina — es el canal documentado de Adobe, y se limpia al terminar |
| `AlwaysPrompts` | Microsoft Print to PDF, Microsoft XPS Document Writer | **no hay forma**: se rechaza antes de empezar |
| `Unknown` | cualquier otra | se intenta con `PrintToFileName` y se avisa en la ventana |

**Por qué "Microsoft Print to PDF" se rechaza y no se automatiza.** Está en el puerto `PORTPROMPT:`:
Windows abre su propio cuadro de diálogo "Guardar salida de impresión como" en *cada* lámina y
`SubmitPrint()` se queda bloqueado hasta que alguien lo responda. Responderlo por código obliga a
buscar la ventana y simular teclado, o sea a apoderarse del primer plano y del teclado en cada
lámina — exactamente lo que dejaba el equipo inutilizable. Y **ocultar la ventana es peor**: el
trabajo se bloquea igual, solo que ahora sin que se vea por qué. Así que el complemento declina y
explica qué hacer.

La ventana muestra, debajo del selector, cómo se va a comportar la impresora elegida — antes de
empezar el lote, no a mitad de camino.

Opciones, de mejor a peor:

1. **Revit 2022 o superior** — API nativa, sin impresora y sin ventanas. Es la mejor opción con
   diferencia.
2. **Adobe PDF**, si tienes Acrobat instalado: silenciosa vía Distiller. Dos ajustes que conviene
   hacer **una sola vez** en Windows → Impresoras y escáneres → *Adobe PDF* → Preferencias:
   - desmarcar **"Ver los resultados de Adobe PDF"** (si no, Acrobat abre cada PDF al terminarlo);
   - desmarcar **"Preguntar por el nombre del archivo"** si estuviera activado.

   Ambos ajustes viven dentro del driver de Adobe (no en un valor de registro plano), así que el
   complemento no puede desactivarlos por código de forma confiable; una vez desmarcados quedan así
   para siempre.
3. **Instalar una impresora PDF silenciosa**: PDF24 Creator, Bullzip PDF Printer, CutePDF Writer,
   PDFCreator, doPDF.
4. **Exportar a DWG**, que no depende de ninguna impresora en ninguna versión.

## Estructura del repositorio

```
src/
  Directory.Build.props            Matriz de versiones de Revit (una sola perilla: RevitVersion)
  GvrTools.Revit.props             Framework, paquetes y símbolos REVITxxxx por versión
  GvrTools.Core/                   Sin Revit ni WPF: nombres, saneado, ajustes, log, resultados
  GvrTools.UI/                     WPF compartido: tema, MVVM, diálogos, íconos vectoriales
  GvrTools.Revit/                  API de Revit: IRevitTool, scheduler, láminas, motores de export
  GvrTools.Tools.BatchExport/      La herramienta de exportación masiva (botón + comando + ventana)
  GvrTools.App/                    IExternalApplication: arma la cinta con lo que encuentre
tests/
  GvrTools.Core.Tests/             Pruebas de la lógica pura, corren sin Revit
deploy/
  GvrTools.addin                   Manifiesto de referencia para instalación manual
scripts/
  install-addin.ps1                Compila e instala en todas las versiones detectadas
docs/
  ARCHITECTURE.md                  Por qué está dividido así y cómo agregar una herramienta
build/                             Salida por versión (ignorada por git)
```

## Agregar una herramienta nueva

Resumen: crear `src/GvrTools.Tools.MiHerramienta/`, implementar `IRevitTool` y un `IExternalCommand`,
y referenciar el proyecto desde `GvrTools.App.csproj`. No hay que modificar ningún archivo existente
del complemento. Los pasos con código están en
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#cómo-agregar-una-herramienta-nueva).

## Diagnóstico

Si algo falla, lo primero que hay que mirar es el log:

```
%LOCALAPPDATA%\GVR\GvrTools\logs\gvrtools-AAAAMMDD.log
```

Un archivo por día, se conservan los últimos 10. Registra qué versión de Revit se cargó, qué
estrategia de exportación se eligió, y cada error con su traza completa. Las preferencias de cada
herramienta se guardan aparte, en `%APPDATA%\GVR\GvrTools\<herramienta>.settings`; borrar ese archivo
devuelve la herramienta a sus valores por defecto.

## Limitaciones conocidas

- No genera un PDF único combinado: produce un archivo por lámina, que es el pedido original. Con la
  API nativa (2022+) agregarlo es directo — es un punto de extensión previsto en el motor de PDF.
- En Revit 2021, el emparejamiento automático de tamaño de papel depende de que la impresora reporte
  el tamaño estándar correspondiente; los rótulos muy personalizados caen de respaldo en "ajustar a
  página".
- En Revit 2021 la exportación PDF sí detiene a Revit un instante por lámina, mientras se comprueba
  que la impresora terminó de escribir el archivo (unas décimas de segundo con una impresora que
  funciona; hasta 15 s por lámina si algo va mal). Es inevitable: el trabajo de impresión vive en el
  hilo de la API. Con la API nativa (Revit 2022+) esa espera no existe.
- Los tokens de revisión leen los parámetros estándar de "revisión actual" de la lámina; si el
  proyecto no usa revisiones, quedan vacíos (y su separador desaparece del nombre).

## Rama de trabajo

El desarrollo vive en la rama `dev_deyvy`.
