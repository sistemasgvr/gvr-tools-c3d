# Assets del instalador

## Branding (`SetupIcon.ico`, `WizardImage.bmp`, `WizardSmallImage.bmp`)

Generados a partir de `src/GvrTools.UI/Icons/Escudo_GVR.png` (el mismo escudo que usan las
ventanas WPF vía `BrandIcons.Escudo`), sobre el mismo fondo claro del tema (`Gvr.Color.Surface`,
`#F6F7F9`, ver `src/GvrTools.UI/Theme/GvrTheme.xaml`), para que el instalador se vea parte de la
misma familia visual que el add-in.

| Archivo | Uso en Inno Setup | Tamaño |
| --- | --- | --- |
| `SetupIcon.ico` | `SetupIconFile` / `UninstallDisplayIcon` -- ícono del `.exe` y de "Agregar o quitar programas" | Multi-resolución (16/32/48/256 px, frames PNG con transparencia) |
| `WizardImage.bmp` | `WizardImageFile` -- panel vertical de las páginas Bienvenida/Fin | 240×459 (tamaño recomendado por Inno para no verse borroso en pantallas de alto DPI) |
| `WizardSmallImage.bmp` | `WizardSmallImageFile` -- logo pequeño en la esquina de cada página interna | 147×147 (tamaño recomendado por Inno) |

Si el logo cambia, no hay que editar estos archivos a mano: hay un generador chico en
`.NET`/`System.Drawing` que hace el recorte, el fondo y el ícono multi-resolución. No vive en el
repo (era un script de una sola vez), pero se reconstruye en 5 minutos:

```powershell
# Desde una carpeta cualquiera, con dotnet en el PATH:
dotnet new console -o gvr-branding
# reemplazar Program.cs con la lógica: cargar el PNG, componer sobre #F6F7F9 centrado
# (62% del lado más chico para WizardImage, 82% para WizardSmallImage), guardar como BMP de
# 24 bpp; para el .ico, generar frames PNG de 16/32/48/256 y empaquetarlos en el contenedor ICO
# (formato "PNG in ICO", soportado desde Windows Vista).
dotnet run -- ruta\Escudo_GVR.png installer\assets
```

`WizardImage.bmp` y `WizardSmallImage.bmp` son BMP planos (sin canal alfa) a propósito: los
paneles del wizard de Inno no componen transparencia de forma confiable, así que el logo se aplana
sobre el fondo claro en vez de dejarlo transparente.
