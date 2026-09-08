# Runbook: licencias (cobro y entrega manual)

Sin Stripe ni SMTP en v1. El dinero y el envío de la clave van por transferencia / factura / WhatsApp / correo tuyo.

## Tras confirmar el pago

1. En el **panel admin**: crea o abre el **Cliente** (empresa o persona). Anota en notas el pago (monto, fecha, referencia).
2. **Licencias → Nueva**: elige cliente, plan, `MaxUsers`, `ValidUntil` → **copia la clave** `GVR-…`.
3. Envía **tú** al cliente (correo/WhatsApp):
   - el enlace de descarga del instalador: `https://<dominio-license>/download` (o el que muestra **Actualizaciones** en el admin tras publicar el Setup)
   - la clave `GVR-…`
   - que en Revit abran **Cuenta / Licencia**, peguen la clave y usen **nombre + correo de cada colaborador**

## Escenarios

| Caso | Cliente | Plan | MaxUsers | ValidUntil |
| --- | --- | --- | --- | --- |
| Una sola persona | Él/ella | Starter/Pro | **1** | +1 mes / +1 año |
| Empresa, N colaboradores | La empresa | El acordado | **N** | Según contrato |
| De por vida | Persona o empresa | El acordado | 1 o N | Fecha lejana (p. ej. 2099-01-01) |

Extras puntuales (más cuota, DWG en Starter, etc.): edita la licencia → **overrides** (no hace falta un plan nuevo).

## Operación diaria

- **Más asientos** tras pago extra: edita la licencia y sube `MaxUsers`.
- **Renovar** suscripción: edita `ValidUntil`.
- **Moroso**: **Suspender**. El add-in corta en el próximo heartbeat (gracia offline máx. ~7 días).
- **PC atascado / cambio de máquina**: en la licencia, **Liberar** el device; o el usuario usa **Desactivar este PC**.
- **Colaborador que se va**: **Miembros** del cliente → desactivar esa persona.

## Plan Free vs Trial

- **Free** (`code = free`): freemium permanente vía `activate-free`. Mantener **activo**.
- **Trial** (`code = trial`, “Trial 14 días”): **eliminado** de la BD (fila borrada). Freemium = solo Free. Si necesitás prueba de pago, emití Starter/Pro con `valid_until` corto.
- Limpieza de licencias/clientes (conserva admins, borra trial): [`server/scripts/cleanup-licenses-clients.sql`](../server/scripts/cleanup-licenses-clients.sql) + [`server/scripts/README.md`](../server/scripts/README.md).

## Plan Free (UI_FREEMIUM_PLAN.md §2.2/§4.1)

Al instalar sin ninguna clave, el add-in llama solo a `POST /v1/activate-free` y queda licenciado
con el plan `code = free` -- sin intervención tuya. Ese plan **siempre existe** (el servidor lo crea
solo al primer arranque si falta) y sus límites viven **solo** en Admin → Planes, igual que Starter/Pro.

- **Cambiar la oferta free** (subir cuota, habilitar DWG, etc.): Admin → Planes → editar el plan
  `Free`. No hace falta redeploy del cliente ni del Setup; el add-in lo aplica en el próximo
  heartbeat/reinicio.
- **No borrar el plan `free`**. Si por error se desactiva (`IsActive = false`), `activate-free`
  empieza a devolver 503 "registro gratuito temporalmente no disponible" -- reactívalo en Admin →
  Planes para restaurar el alta automática.
- **Camino free → pago**: el cliente pega una clave `GVR-…` de soporte igual que cualquier
  activación (botón **Cambiar plan** en la tool o **Cuenta / Licencia** en la cinta). Ese PC pasa a
  la licencia de pago; la licencia free que tenía queda huérfana (sin dispositivos), no se borra sola.
- **Reinstalación / borrar archivos locales**: mismo fingerprint de máquina → misma licencia free de
  siempre (o la de pago si ya hizo upgrade). Nunca genera una segunda free para el mismo PC.
- **"Liberar" (kick) un dispositivo**: al perder la sesión, el add-in intenta el plan free automáticamente
  antes de pedir reactivación manual -- así nadie se queda sin ninguna herramienta. Si en cambio
  **suspendiste** la licencia (sin liberar el device), ese intento de free también falla sobre la
  misma licencia suspendida y sí pide reactivar a mano, como corresponde. "Liberar" es también el
  camino correcto para resetear un dispositivo de prueba de vuelta a cero.
- **Auditar altas free**: Auditoría (o el widget del dashboard) muestra `license.activate_free`
  (alta) y `security.activate_free_denied` (rechazado -- plan free ausente/desactivado); `Licencias`
  agrupa las free bajo el cliente contenedor **"GVR Free installs"**.
- **Abuso**: `/v1/activate-free` tiene rate limit de 5 solicitudes / 10 min por IP (además de que un
  fingerprint repetido nunca crea una licencia nueva). Señales más finas (spikes por IP, mismo
  fingerprint en variantes) quedan para la Fase 4 del plan.

## Esquema (Postgres)

En **Development**, la API aplica migraciones EF al arrancar (`Database.Migrate()`). En **Production**, no: usa `dotnet ef database update` con `ConnectionStrings__Postgres` (ver [`DEPLOY_EASYPANEL.md`](DEPLOY_EASYPANEL.md) y `server/README.md`).

## Deploy producción

Checklist EasyPanel (Docker, env, MinIO, dominio, primer release): [`DEPLOY_EASYPANEL.md`](DEPLOY_EASYPANEL.md).

## Qué no hace el sistema (a propósito)

- No cobra solo.
- No envía la key por correo automático.
- No hay portal self-service del cliente en v1.
