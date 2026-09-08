# vendor/

Código fuente completo de terceros, clonado tal cual (no assets compilados sueltos -- eso vive en
`GvrLicense.Api/wwwroot/lib`).

## adminlte/

AdminLTE **v4.8.5** completo (MIT, [ColorlibHQ/AdminLTE](https://github.com/ColorlibHQ/AdminLTE)),
clonado del tag `v4.8.5` sin el historial de git. Es la referencia para construir/ampliar el panel
`/admin/*`: casi 50 páginas de ejemplo ya funcionando (widgets, tablas, formularios, layouts,
Mailbox, UI Elements, gráficos con ApexCharts, etc.) que se pueden copiar y adaptar a Razor Pages
según haga falta, sin tener que reinventar el markup de AdminLTE desde cero cada vez.

- `dist/` -- HTML compilado de todas las páginas de ejemplo + CSS/JS listos para usar. Empezar
  aquí para ver una página funcionando antes de portarla.
- `src/scss/` `src/ts/` -- fuente de `adminlte.css`/`adminlte.js`, por si algún día hace falta
  recompilar con una paleta de colores propia en vez de la de fábrica.
- `docs/` -- documentación de componentes.

Lo que ya se portó a Razor Pages vive en `GvrLicense.Api/Pages/Admin/` y usa los assets
vendorizados en `GvrLicense.Api/wwwroot/lib` (copia de `dist/css`, `dist/js`, `dist/assets` más
Bootstrap 5, Bootstrap Icons, OverlayScrollbars y la fuente Source Sans 3 -- las mismas
dependencias que `dist/*.html` carga por CDN, aquí servidas localmente).

Para portar una página nueva: buscarla en `dist/` (p. ej. `dist/tables/data.html` para una tabla
con DataTables, `dist/pages/profile.html` para un patrón de perfil), copiar el `<main class="app-content">`
hacia adentro, y pegarlo dentro de `@RenderBody()` de una `.cshtml` nueva bajo `Pages/Admin/` --
el `<head>`/navbar/sidebar ya los pone `_Layout.cshtml`.
