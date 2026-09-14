# Plotters empaquetados (PC3 HQ)

Coloca aquí el archivo:

```text
DWG To PDF_HQ_.pc3
```

Es una copia del plotter **DWG To PDF.pc3** de AutoCAD con propiedades personalizadas
(DPI alto, p. ej. 1200) para sellos/CIP legibles. GVR Tools **no genera** el .pc3 por API:
solo lo copia a la carpeta de Plotters del usuario cuando pulsa “Instalar plotter HQ…”.

Cómo obtenerlo (una vez, en AutoCAD):

1. Imprimir → elige `DWG To PDF.pc3` → Propiedades → Propiedades personalizadas.
2. Sube la calidad/DPI según necesites.
3. Guardar como → `DWG To PDF_HQ_.pc3` en esta carpeta (o en la carpeta Plotters del sistema).

`scripts/pack-release.ps1` incluye `deploy/plotters/*` en el ZIP bajo `GvrTools.bundle/Plotters/`
cuando el archivo exista.
