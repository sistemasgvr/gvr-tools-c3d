# Plan: UI tipo ProSheets + freemium + anti-abuso

Objetivo: llevar Batch Export y el flujo de licencia a una experiencia cercana a **DiRoots ProSheets** (wizard por pasos, botón tipo *Unlock Premium*, uso en el footer, diálogo de éxito), sin romper la arquitectura actual (WPF + License API).

Relacionado: [`LICENSING_PLAN.md`](LICENSING_PLAN.md), [`ARCHITECTURE.md`](ARCHITECTURE.md), [`RUNBOOK_LICENSING.md`](RUNBOOK_LICENSING.md).

---

## 1. Situación actual (baseline)

| Área | Hoy | Gap vs ProSheets |
| --- | --- | --- |
| UI stack | WPF + `GvrTheme.xaml`; WinForms solo para diálogos de sistema | Sin Fluent / shell moderno; look “funcional” |
| Batch Export | **Una sola página** larga (`BatchExportWindow.xaml`) | Falta wizard **Selection → Format → Create** |
| Licencia | Activar con clave; sin plan free automático | Falta CTA **Unlock / Cambiar plan** en la tool |
| Uso | Cuota solo en **Cuenta / Licencia** | Falta **footer** con “N de M láminas usadas” |
| Post-export | Expander + `StatusText` | Falta **ventana de exportación exitosa** |
| Primer uso | Sin licencia → tool oculta / fuerza Activar | Falta **plan gratuito** al instalar con registro en servidor |
| Auditoría | Trigger de status, kick, deactivate; **activate no se audita** | Falta rastro de altas free, upgrades y señales de abuso |

> **Estado (gaps de UI cerrados en código):** wizard Selection → Format → Create con tabs clicables; CTA Cambiar plan unificado; footer `Usadas X de Y`; candados por formato; plan free + activate-free + auditoría (fases 1–5). Quedan checklist QA en Revit y deploy (Setup/Docker/release) cuando publiques.

Archivos clave hoy:

- UI: [`src/GvrTools.UI/Theme/GvrTheme.xaml`](../src/GvrTools.UI/Theme/GvrTheme.xaml), [`src/GvrTools.Tools.BatchExport/Views/BatchExportWindow.xaml`](../src/GvrTools.Tools.BatchExport/Views/BatchExportWindow.xaml)
- Licencia: [`ActivateLicenseWindow.xaml`](../src/GvrTools.Licensing/Activation/ActivateLicenseWindow.xaml), [`AccountLicenseWindow.xaml`](../src/GvrTools.Licensing/Activation/AccountLicenseWindow.xaml), [`LicenseClient.cs`](../src/GvrTools.Licensing/LicenseClient.cs)
- Servidor: [`LicenseEngine.cs`](../server/src/GvrLicense.Api/Services/LicenseEngine.cs), [`V1Endpoints.cs`](../server/src/GvrLicense.Api/Endpoints/V1Endpoints.cs)

---

## 2. Decisiones de producto (fijadas para este plan)

### 2.1 Stack de UI (una sola capa para todos los años)

- **Mantener WPF** (add-in Revit). No migrar a WinForms ni WinUI/MAUI.
- **Requisito duro:** el mismo look y las mismas DLLs de UI deben funcionar en **Revit 2021–2027** (`net48` + `net8`/`net10`) **dentro del proceso de Revit**, no solo en un `.exe` de prueba.
- NuGets Fluent “de escritorio” (WPF UI / ModernWpf) suelen:
  - fallar o pelear con el host WPF de Revit (temas globales, `Application`, chrome Mica), y/o
  - obligar a forks por año (p. ej. paquetes “Revit.Wpf.Ui”) → **dos stacks** = no aceptable aquí.
- **Decisión de este plan:** no depender de un NuGet Fluent como base del producto.
  - **Camino elegido:** extender [`GvrTheme.xaml`](../src/GvrTools.UI/Theme/GvrTheme.xaml) hacia un **Fluent-like propio** (tipografía, cards, tabs de pasos, botones, footer, DataGrid) con controles WPF estándar. Cero dependencia de host aparte de WPF.
  - Opcional más adelante: un NuGet **solo si** el spike demuestra carga limpia en **2021 y 2025+** sin fork por TFM; si no pasa, se descarta.
- Branding GVR (escudo, azul `#1565C0` o acento acordado). No copiar naranja DiRoots.

### 2.2 Modelo freemium (plan `free` dinámico y permanente)

El plan gratuito **no lleva cuotas ni features hardcodeados en el add-in**. Todo lo comercial vive en el **plan registrado en el servidor** (igual que Starter/Pro), editable en **Admin → Planes**.

| Concepto | Comportamiento |
| --- | --- |
| Plan `free` | **Siempre existe** en la tabla `plan` (código estable `free`, `IsActive = true`). Sus `features` (cuota, formatos, límites de lote, seats, etc.) se configuran en admin cuando quieras cambiar la oferta |
| Quién define límites | Solo el JSON/features del plan en Postgres. El cliente solo aplica entitlements firmados |
| Primer arranque | Si no hay `license.dat` válido → **`POST /v1/activate-free`** con fingerprint + nombre máquina (+ nombre/correo si ya se pidió) |
| Servidor | Resuelve el plan con `code = free` **activo**; emite o reutiliza una licencia ligada a ese plan + fingerprint; blob firmado como `/v1/activate` |
| Anti-reinstalación | Mismo fingerprint → **misma** licencia free (o la de pago si ya upgradó). Borrar archivos locales **no** crea otra free ni resetea cuota |
| Upgrade | Usuario pega clave `GVR-…` de soporte. Ese device pasa a la licencia de pago |
| Cobro | Manual (sin Stripe). Soporte envía la key |
| Cambios de oferta free | Editas el plan `free` en admin (p. ej. subes cuota o habilitas DWG). Licencias free **nuevas** y heartbeats/ensure counters respetan el plan actual; documentar en runbook si los counters ya abiertos del mes conservan `QuotaLimit` hasta el siguiente periodo |

Principio anti-crack: el free **también habla con el servidor**. Lo que vive solo en el cliente se puede parchear; plan y cuota reales viven en Postgres + blob firmado.

**Invariante operacional:** no borrar el plan `free`. Se puede endurecer o aflojar; si se desactiva (`IsActive = false`), `activate-free` debe fallar con mensaje claro (“registro free temporalmente no disponible”) — no inventar un plan fantasma en código.

**Free vs Trial:** el plan `free` es el freemium. El plan `trial` se **elimina** de la BD (cleanup + seed). Ver runbook y `server/scripts/cleanup-licenses-clients.sql`.

### 2.3 UX tipo ProSheets (sin clonar marca)

| Elemento ProSheets | Equivalente GVR |
| --- | --- |
| Tabs Selection / Format / Create | Wizard de 3 pasos en Batch Export |
| Unlock Premium | Botón **Cambiar plan / Activar licencia** en header de la tool |
| “N of M exports used” | Footer: láminas usadas / límite del mes (+ enlace upgrade si free) |
| Diálogo post-create | Ventana **Exportación exitosa** (resumen, abrir carpeta, CTA upgrade si free) |

---

## 3. UX objetivo (add-in)

### 3.1 Shell Batch Export (wizard)

```
┌─────────────────────────────────────────────────────────────┐
│ [Escudo GVR]  GVR Tools · Batch Export    [Cambiar plan ★] │
│                                 v1.0.0                       │
├─────────────────────────────────────────────────────────────┤
│  (1) Selección   (2) Formato   (3) Crear                    │
├─────────────────────────────────────────────────────────────┤
│  … contenido del paso activo …                              │
├─────────────────────────────────────────────────────────────┤
│ N láminas · M vistas · Total: K                             │
│ Plan Free · 12 / Y láminas este mes           [Atrás][Sig.] │
└─────────────────────────────────────────────────────────────┘
```

(Y = límite del plan `free` o de pago según entitlements; no hardcode en UI.)

**Paso 1 — Selección:** grid de láminas, filtros, buscar, select all/none (lo que ya existe en la zona superior).

**Paso 2 — Formato:** PDF / DWG / ambos, opciones. Features no incluidas en el plan se muestran **bloqueadas** (candado / estrella) y al clic abren “Cambiar plan”.

**Paso 3 — Crear:** carpeta destino, naming, barra de progreso, lista de cola. Botón primario **Exportar / Crear**.

Navegación: Atrás / Siguiente; en paso 3 el primario dispara el lote. Validaciones de cuota/formatos **antes** de pasar a Crear o al pulsar Exportar (reutilizar `TryValidateLicenseQuota`).

### 3.2 Botón “Cambiar plan” (Unlock Premium)

Abre una ventana modal (evolución de Activate + fragmento de Account):

1. Resumen del plan actual (Free / Starter / Pro, vencimiento, cuota).
2. **Contacto soporte** (`support_email` del servidor / hint en entitlements).
3. Campo para **pegar licencia** `GVR-…` (+ nombre + correo del usuario, como hoy).
4. Texto: “Si ya pagaste, soporte te envió una clave. Pégala aquí.”
5. Acciones: Activar · Desactivar este PC · Cerrar.

Ribbon **Cuenta / Licencia** se mantiene como acceso global; el botón del header es el CTA visible dentro de la tool.

### 3.3 Footer de uso

- Texto: `Usadas X de Y este mes` o `Ilimitado` si `quota.sheets_per_month = -1`.
- Si plan free y cerca del límite: aviso suave + enlace “Cambiar plan”.
- Fuente de datos: remaining del entitlement blob / heartbeat (ya existe en Account); Batch Export debe **leer y refrescar** la misma fuente, no solo mostrar errores al fallar.

### 3.4 Ventana de exportación exitosa

Tras un lote con ≥1 lámina OK:

- Título: “Exportación completada”.
- Resumen: N OK / N fallidas, carpeta destino.
- Botones: **Abrir carpeta** · **Cerrar** · (si free) **Cambiar plan**.
- No sustituye el detalle por lámina (puede seguir en expander o “Ver detalle”).

---

## 4. Backend: free + auditoría anti-abuso

### 4.1 Plan `free` dinámico + endpoint

1. **Garantizar plan `free` permanente**
   - Migración/seed: fila `plan` con `code = free`, `IsActive = true`, y un set **inicial** de features (editable después en Admin → Planes; el seed solo evita un entorno vacío).
   - El add-in **nunca** asume números fijos (ni “50 láminas”, ni formatos). Lee el blob.
   - Admin puede cambiar cuota, formatos, `limit.sheets_per_batch`, etc. sin redeploy del cliente.
2. **Cliente contenedor** (p. ej. Customer “GVR Free installs”) para agrupar licencias free; seats/`CompanyUser` según lo que diga el plan `free` (`MaxUsers` / `seat.max_devices_per_user`).
3. Endpoint con rate-limit estricto:

   `POST /v1/activate-free`

   Body: fingerprint, machine display name, (opcional) fullName, email.

   Reglas:

   - Resolver plan: `Plans.Single(p => p.Code == "free" && p.IsActive)`. Si no hay → 503/403 con mensaje operable.
   - Si ya existe device con ese fingerprint → devolver entitlements de **esa** licencia (free o paid).
   - Si no existe → crear license apuntando al plan `free` actual + device + counters → blob firmado.
   - Rate limit por IP + fingerprint (completar el `AddRateLimiter` vacío de hoy).
   - **No** crear una segunda free en el mismo PC.

4. Upgrade con key pagada: flujo `/v1/activate` actual; al activar key de pago en un device free, **mover** el device a la licencia de pago (o liberar free y bindear a paid). Documentar regla exacta en implementación.

### 4.2 Auditoría y detección de violaciones

Ampliar el rastro (hoy incompleto):

| Evento | Action sugerida | Quién |
| --- | --- | --- |
| Alta free | `license.activate_free` | API |
| Activate con key | `license.activate` | API (hoy **no** se escribe) |
| Deactivate / kick | ya existen | — |
| Cambio status | `license_status_changed` | trigger |
| Heartbeat sospechoso | `security.heartbeat_rejected` (opcional) | API |
| Activate-free rechazado (abuso) | `security.activate_free_denied` | API |

Campos / señales a conservar o cruzar en admin (dashboard o página Auditoría):

- `OccurredAtUtc` (hora de registro / evento)
- fingerprint (hash), device display name
- license key / id, plan code
- actor (system / admin / email usuario)
- IP (si se añade a audit details JSON en activate*)
- conteo: N activates free desde misma IP en 24 h; N fingerprints → misma key; reloj cliente vs servidor (skew)

**UI admin (fase posterior del plan):** widget o filtros “Señales de riesgo” (mismo fingerprint en varias licencias free, spikes de activate-free, gracia offline abusada). No bloquea el rediseño de UI del add-in, pero el **logging debe entrar en la misma fase que activate-free**.

### 4.3 Datos ya útiles

- `Device.ActivatedAtUtc` / `LastSeenUtc`
- `UsageEvent.OccurredAtUtc` + `ReceivedAtUtc`
- `AuditLog` + `AuditActionDescriber` (extender labels)
- Dashboard admin ya tiene gráficos de uso; se puede añadir panel de “activaciones free recientes”

---

## 5. Fases de implementación

### Fase 0 — Spike UI multi-año (1–2 días)

- [x] Confirmar **GvrTheme Fluent-like propio** como stack único (boceto shell wizard en 2021 y 2025). *(Se implementó directo el shell real en vez de un boceto separado; ver Fase 1.)*
- [ ] Solo si hay tiempo: prueba opcional de un NuGet Fluent en **ambos** hosts; si falla cualquiera → descartado (no dual-stack). *(Omitido a propósito: el tema propio ya cumple §2.1, no hace falta gastar tiempo en la alternativa.)*
- [x] Boceto XAML del shell wizard (3 pasos) sin lógica nueva. *(Fusionado con Fase 1: se hizo el wizard real de una vez.)*

**Criterio de salida:** §2.1 cerrado (tema propio) + captura del shell vacío en al menos un Revit `net48` y uno `net8+`. ✅ Compila limpio en 2022–2027 (net48/net8/net10); 2021 pendiente de build solo por lock de proceso, no por código.

### Fase 1 — Wizard Batch Export + footer + éxito ✅

- [x] Refactor `BatchExportWindow` a pasos Selection / Format / Create (ViewModel: `WizardStep`, comandos `GoNextCommand`/`GoBackCommand`).
- [x] Footer con selection summary + **cuota** (bind a entitlement remaining).
- [x] Ventana `ExportSuccessWindow` post-lote.
- [x] Mantener preferencias / naming / motores Revit sin cambios de comportamiento. *(Verificado: ningún binding/comando existente cambió, solo se agruparon bajo Visibility por paso.)*

**Criterio de salida:** mismo flujo de exportación usable en wizard; cuota visible sin abrir Cuenta. ✅

Footer: `Plan {code} · Usadas X de Y este mes` (o `ilimitado`). El blob incluye companion `{quotaCode}.limit` desde el License API.

### Fase 2 — CTA Cambiar plan (Unlock) ✅

- [x] Botón header → ventana unificada upgrade/activar (soporte + pegar key + desactivar). *(`LicenseUi.ShowChangePlan` / `AccountLicenseWindow` con formulario inline.)*
- [x] Features bloqueadas en paso Formato con candado/estrella por opción; clic abre Cambiar plan.
- [x] CTA también en footer y en diálogo de éxito.
- [x] Tabs del wizard clicables (Selección / Formato / Crear).

**Criterio de salida:** usuario puede ver límites y pegar key de soporte sin salir a la cinta. ✅

### Fase 3 — Plan free dinámico + activate-free + anti-reinstall ✅

- [x] Seed/migración: plan `code=free` **siempre presente**; features iniciales editables en Admin → Planes. *(Seed idempotente en `Program.cs`, corre en cada arranque -- no solo Development, porque es dato, no esquema; nunca sobreescribe un plan `free` que ya exista.)*
- [x] `POST /v1/activate-free` lee el plan `free` activo (no hardcode de cuota/formatos) + rate limit. *(`LicenseEngine.ActivateFreeAsync`; rate limit fijo 5/10min por IP vía `AddRateLimiter`.)*
- [x] Cliente: al arrancar sin cache → activate-free; ribbon/tools según entitlements devueltos. *(`LicenseRuntime.WarmupAsync` -- se agregó sin tocar el orden existente: `GvrApplication.OnStartup` ya esperaba el warmup ANTES de armar la cinta, así que el ribbon ya refleja el plan free recién obtenido sin cambios ahí.)*
- [x] Reglas fingerprint: reinstall no regenera licencia free nueva. *(Mismo fingerprint ya registrado → reusa esa licencia, sea free o de pago; nunca crea una free nueva.)*
- [x] Auditoría `license.activate_free` / `license.activate`. *(Se agregó también `security.activate_free_denied` para el caso "plan free ausente/desactivado".)*
- [x] Actualizar [`RUNBOOK_LICENSING.md`](RUNBOOK_LICENSING.md) (editar oferta free en admin; camino free → pago).

**Criterio de salida:** ✅ Verificado en vivo contra producción (autorizado por el usuario): `POST /v1/activate-free` corrido localmente contra la Postgres real.

- 1ª llamada (fingerprint nuevo): **encontró y corrigió un bug real** -- `EnsureCurrentPeriodCountersAsync` corría antes de guardar la License nueva, y su INSERT crudo en `usage_counter` violaba la FK (`23503`) porque la fila `license` todavía no existía. Se movió `SaveChangesAsync` antes de `EnsureCurrentPeriodCountersAsync`.
- 2ª llamada (mismo fingerprint): reusó exactamente el mismo `device_id`/licencia -- cero duplicados, confirmado también por consulta directa a la tabla.
- `usage_counter` se creó con `quota_limit=20` (el del plan free sembrado), `consumed=0`.
- `audit_log` tiene exactamente 1 fila `license.activate_free` para el fingerprint de prueba (no 2, aunque hubo 2 activaciones).
- Rate limit: 3 llamadas más pasaron, la 4ª y 5ª devolvieron `429` -- política de 5/10min por IP funcionando.
- Datos de prueba (licencia, dispositivo, contador, auditoría, company_user) **borrados** después de verificar; el cliente contenedor "GVR Free installs" se dejó (es el que usarán los altas free reales).
- 21/21 tests del server sin regresiones tras el fix.

### Fase 4 — Señales de seguridad en admin ✅

- [x] Persistir IP / detalles en activates. *(Ya estaba en activate-free/denied desde la Fase 3; se agregó también a `license.activate` con key de pago, que no lo tenía.)*
- [x] Vista o filtros de riesgo en Auditoría / Dashboard. *(Panel "Señales de riesgo" en Admin → Auditoría: IPs con varios intentos de alta free, y el mismo fingerprint repetido en más de una licencia. Calculado sobre las mismas 500 filas que ya carga la página -- sin consulta nueva pesada.)*
- [x] Alertas simples (umbrales) — sin ML en v1. *(Umbral fijo: IP con ≥3 intentos de activate-free, o cualquier `security.activate_free_denied`. El panel de riesgo no aparece si no hay nada que mostrar.)*
- [x] Panel "activaciones free recientes" en el dashboard. *(No se agregó uno nuevo: el dashboard ya tenía "Auditoría reciente" -- genérico, así que `license.activate_free` aparece ahí solo en cuanto empiece a pasar, sin duplicar UI.)*

**Criterio de salida:** soporte puede ver activaciones free recientes y anomalías por fingerprint/IP. ✅ Verificado corriendo `Audit/Index.OnGetAsync()` directo contra producción (sin excepción, `Rows=4 RiskyIps=0 SharedFingerprints=0` -- correcto, no hay señales porque no hay abuso real todavía). 21/21 tests del server sin regresiones.

### Fase 5 — Pulido visual + release ✅ (código), ⏳ (deploy queda para cuando decidas)

- [x] Aplicar el tema Fluent-like de `GvrTheme` a Activate / Account / Success / Update. *(Activate/Account/Update ya lo usaban de una fase anterior a este plan; Success (`ExportSuccessWindow`) se creó ya con el tema en la Fase 1.)*
- [x] Iconografía, tipografía, estados vacíos. *(Se agregó el escudo GVR -- que ya tenían BatchExport/ExportSuccess -- a Activate/Account/Update, que no lo tenían: consistencia visual en las 5 ventanas del add-in. Tipografía ya venía de `GvrTheme`. Estados vacíos: BatchExport ya avisa cuando el proyecto no tiene láminas; el panel de resultados arranca colapsado en vez de mostrar una tabla vacía.)*
- [ ] Rebuild multi-año + Setup + imagen Docker + release en admin. *(Rebuild multi-año hecho en cada fase, ver abajo. Setup/Docker/release **no** se hicieron a propósito: son acciones de despliegue hacia producción real, quedan para cuando el usuario decida publicar.)*
- [ ] Checklist QA (abajo). *(Necesita manos en Revit real; los pasos automatizables ya se verificaron por otras vías durante las Fases 1-4.)*

---

## 6. Impacto por capa

| Capa | Cambios principales |
| --- | --- |
| `GvrTools.UI` | Tema Fluent / recursos compartidos, controles de paso, footer bar |
| `GvrTools.Tools.BatchExport` | Wizard, footer cuota, success dialog, CTA plan |
| `GvrTools.Licensing` | Activate-free client, ventana Cambiar plan unificada, `QuotaLimit` + `QuotaDisplay` |
| `GvrTools.App` | Warmup: si no licensed → intentar free antes de ocultar tools |
| `GvrLicense.Api` | Endpoint free resolviendo plan `free` dinámico, audit, rate limit, seed plan permanente |
| Admin | Edición del plan `free` como cualquier plan; labels auditoría; luego señales de riesgo |
| Docs / runbook | Free → pago; operación anti-abuso |

---

## 7. Fuera de alcance (v1 de este plan)

- Portal self-service del cliente / Stripe.
- SMTP automático de keys.
- Clonar pixel-perfect ProSheets / colores DiRoots.
- Scheduling assistant de ProSheets.
- WinUI 3 / Avalonia / rewrite completo fuera de WPF.
- Floating seats (sigue node-locked por fingerprint + seats por persona).

---

## 8. Riesgos y mitigaciones

| Riesgo | Mitigación |
| --- | --- |
| NuGet Fluent incompatible con `net48` / proceso Revit | **No usarlo como base**; tema Fluent-like en `GvrTheme` (un stack para 2021–2027) |
| Dual-stack UI por año | Prohibido en este plan |
| Admin borra o desactiva plan `free` | `activate-free` falla con error claro; runbook: el plan `free` no se elimina |
| Abuso de activate-free (bots) | Rate limit IP+fingerprint; auditoría; umbrales + ban fingerprint |
| Usuario free offline forever | Misma gracia 7 días; sin heartbeat no se renueva cuota/plan |
| Doble conteo al migrar free→paid | Transacción: mover device, no duplicar counters mal |
| Wizard rompe flujos actuales | Un solo UI wizard; sin modo “clásico” salvo rollback de emergencia |

---

## 9. Checklist QA (antes de publicar Setup)

- [ ] Revit 2021 y 2025: wizard abre, exporta PDF, cancelar, reabrir.
- [ ] PC sin license.dat + online → free; Batch Export visible con cuota free.
- [ ] Desinstalar/reinstalar mismo PC → misma licencia / no reset de abuso obvio.
- [ ] Pegar key Starter/Pro desde “Cambiar plan” → features y footer actualizan (tras restart si sigue siendo necesario).
- [ ] Footer muestra remaining; al agotar cuota bloquea con mensaje claro.
- [ ] Export OK → diálogo éxito + Abrir carpeta.
- [ ] Admin: eventos `activate_free` / `activate` visibles.
- [ ] Offline >7 días → pide reactivación (comportamiento actual).

---

## 10. Orden de trabajo sugerido

1. Fase 0 (spike) → cerrar shell con **GvrTheme** multi-año.
2. Fase 1 (wizard + footer + éxito) — valor UX inmediato con lo que ya hay.
3. Fase 2 (CTA Cambiar plan).
4. Fase 3 (free dinámico server-side) — el cambio de negocio más sensible.
5. Fase 4 (admin seguridad).
6. Fase 5 (release).

---

## 11. Referencias de implementación existentes

- Cuota / consume: `BatchExportViewModel.TryValidateLicenseQuota`, `LicenseEngine.ReportUsageAsync`, `ConsumeQuota.sql`
- Activate pago: `LicenseEngine.ActivateAsync`, `ActivateLicenseViewModel`
- Soporte: `AppSettings.SupportEmail`, `LicenseClient.SupportEmailHint`
- Auditoría: `AuditLogTrigger.sql`, `AuditActionDescriber`, kick en `Licenses/Edit.cshtml.cs`
