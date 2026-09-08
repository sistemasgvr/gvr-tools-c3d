# Exportación masiva — guía de opciones

Referencia rápida para explicar a usuarios qué hace cada control de **GVR Tools → Exportación masiva de láminas**.

La ventana tiene tres pasos: **Selección → Formato → Crear**.

---

## Paso 1 — Selección

Elige qué se va a exportar.

| Opción | Qué hace |
|--------|----------|
| **Láminas** | Lista las láminas del proyecto (lo habitual para entregar planos). |
| **Vistas** | Lista vistas sueltas (plantas, cortes, 3D, detalles…) sin rótulo de lámina. |
| **Todas** | Marca todas las filas visibles (respeta buscador y filtros). |
| **Ninguna** | Quita la marca de las filas visibles. |
| **Invertir** | Intercambia marcadas ↔ desmarcadas (solo visibles). |
| **Pendientes** | Marca solo las que aún no se han exportado desde este PC. |
| **Buscar** | Filtra por número o nombre. No cambia lo ya marcado. |
| **Set** | Muestra solo las láminas de un conjunto guardado en Revit (los del diálogo Imprimir). |
| **Filtro** | Recupera una selección guardada (p. ej. “solo arquitectura”). `(Todos)` muestra todo. |
| **Guardar selección como filtro…** | Guarda las filas marcadas con un nombre para reutilizarlas. |
| **Eliminar** | Borra el filtro guardado (no borra láminas del proyecto). |

---

## Paso 2 — Formato

### Formato de salida

| Opción | Qué hace |
|--------|----------|
| **PDF** | Solo PDF. |
| **DWG** | Solo DWG (AutoCAD). |
| **PDF + DWG** | Ambos (según el plan). |
| **Abrir la carpeta al finalizar** | Al terminar, abre el Explorador en la carpeta de salida. |

En **Revit 2021** el PDF se genera con **PDF24** (debe estar instalada). En **2022+** se usa el exportador nativo de Revit.

---

### PDF — Ubicación en el papel

| Opción | Qué hace | Cuándo usarla |
|--------|----------|---------------|
| **Usar el tamaño de cada lámina** | Activado: cada PDF usa el tamaño del rótulo. Desactivado: **mismo papel fijo para todas** (ANSI D / ~22×34" en 2022+ y en 2021 vía PDF24 si el driver lo ofrece). | Entregas normales → activado. |
| **Centrado** | Dibujo centrado en la hoja. | Recomendado. |
| **Desde una esquina** | Ancla el dibujo desde una esquina. | Cuando necesitas un margen o alineación concreta. |
| **Sin margen** | Pegado a la esquina. | Casos especiales. |
| **Límite de impresora** | Deja margen desde la esquina. **2021:** margen real de la impresora. **2022+:** ~0,25 pulg (la API nativa no tiene equivalente exacto). | Si se va a imprimir en papel físico. |
| **Definido por el usuario** | Desplazamiento manual (X / Y en pulgadas). | Ajustes finos. |

### PDF — Zoom

| Opción | Qué hace |
|--------|----------|
| **Ajustar a la página** | Escala el dibujo para que quepa completo. **Recomendado.** |
| **Zoom %** | Escala fija (p. ej. 50 % = mitad del tamaño, con espacio en blanco). **Hay que seleccionar “Zoom”, no “Ajustar a la página”.** En Revit 2022+ el complemento elige un tamaño de papel estándar para que el zoom se respete (si usa “tamaño de lámina” + ajustar, Revit ignora el zoom). |

### PDF — Líneas ocultas

| Opción | Qué hace |
|--------|----------|
| **Procesamiento vectorial** | Dibujo en vectores: nítido y ligero. **Opción recomendada.** |
| **Procesamiento raster** | Convierte a imagen. Solo si el vectorial falla o hay geometría muy compleja. |

### PDF — Apariencia

| Opción | Qué hace |
|--------|----------|
| **Calidad ráster** | Calidad de imágenes y sombreados. Más alta = mejor aspecto y archivo más pesado. |
| **Color** | **Color** = a color · **Escala de grises** · **Blanco y negro** = solo líneas negras (ideal para imprimir en B/N). |

### PDF — Opciones de limpieza

| Opción | Qué hace | Consejo |
|--------|----------|---------|
| **Vínculos de vista en azul** | Pinta en azul los enlaces entre vistas. | Actívalo si quieres destacar esos vínculos. |
| **Ocultar planos de referencia/trabajo** | No dibuja planos de ref./trabajo. | Suele dejar el PDF más limpio. |
| **Ocultar etiquetas de vista sin referencia** | Quita etiquetas que no apuntan a ninguna vista. | Evita marcas “huérfanas”. |
| **Ocultar cajas de alcance** | No dibuja scope boxes. | Recomendado en entregas. |
| **Ocultar límites de recorte** | Quita el rectángulo de recorte de las vistas. | PDF más limpio. |
| **Reemplazar medio tono con líneas finas** | Convierte grises en líneas finas. | Útil si vas a **imprimir o fotocopiar en blanco y negro**. |
| **Enmascarar líneas coincidentes en bordes** | En zonas recortadas, oculta líneas que caen justo en el borde. | Evita dobles trazos o bordes sucios. |

### PDF — Archivo

| Opción | Qué hace |
|--------|----------|
| **Crear archivos separados** | Un PDF por lámina/vista. Ideal para enviar o revisar planos sueltos. |
| **Combinar en un solo archivo** | Un único PDF con todas las láminas/vistas (cómodo para entregar). |
| **Nombre del archivo** | Nombre del PDF combinado. Acepta códigos: `{ProjectTitle}`, `{ProjectNumber}`, `{ProjectName}`, `{ClientName}`, `{Date}`. |

---

### DWG

| Opción | Qué hace |
|--------|----------|
| **Configuración** | Usa una configuración DWG ya guardada en el proyecto (capas, grosores…). `(Personalizada)` usa las opciones de abajo. |
| **Versión DWG** | Versión de AutoCAD. Elige la más antigua que necesiten tus destinatarios. |
| **Combinar vistas** | Todas las vistas de la lámina en un solo modelo. Si se desmarca, cada vista va en su propio layout. |
| **Coordenadas compartidas** | Usa coordenadas compartidas (p. ej. topográficas). Actívalo si el DWG se alineará con otros planos georreferenciados. |
| **Exportar imágenes** | Además del DWG, crea un PNG por lámina (vista previa). |

---

## Paso 3 — Crear

| Opción | Qué hace |
|--------|----------|
| **Carpeta destino** | Dónde se guardan los archivos. |
| **Subcarpeta del proyecto** | Crea sola una carpeta con el nombre del proyecto. |
| **Patrón de nombre** | Cómo se nombran los archivos por lámina (tokens como `{SheetNumber}`, `{SheetName}`, etc.). |
| **Exportar** | Lanza el lote. |
| **Cancelar** | Para al terminar la lámina actual (no corta el archivo a medias). |
| **Abrir carpeta** | Abre la carpeta de salida. |

---

## Valores recomendados (entrega típica)

Para la mayoría de entregas en PDF:

1. **Usar el tamaño de cada lámina** — activado  
2. **Centrado**  
3. **Ajustar a la página**  
4. **Procesamiento vectorial**  
5. **Calidad ráster** — Alta  
6. **Color** — según cliente (Color o Blanco y negro)  
7. Ocultar: planos de ref., etiquetas sin referencia, cajas de alcance, límites de recorte — **activados**  
8. **Enmascarar líneas coincidentes** — activado  
9. Archivos separados **o** combinado, según cómo lo pidan  

---

## Notas por versión de Revit

| Versión | PDF |
|---------|-----|
| **2021** | Requiere **PDF24**. “Combinar” imprime cada lámina y las une. |
| **2022+** | Exportador nativo de Revit. “Combinar” es una sola exportación. |

---

*Documento de soporte interno / atención a usuarios. Los textos de ayuda al pasar el mouse en la app siguen el mismo criterio.*
