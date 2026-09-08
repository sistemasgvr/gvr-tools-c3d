# Admin

Panel dueño (docs/LICENSING_PLAN.md, Pieza 5), servido bajo `/admin/*` en este mismo proyecto --
Razor Pages server-rendered, no Blazor Server (ver Pieza 1: es un panel de uso ocasional y solo
tuyo, evita depender de un WebSocket persistente contra Traefik). Auth: cookie + TOTP obligatorio
(Otp.NET). Carpeta reservada, sin páginas todavía -- se implementa en Fase 1.

Pantallas previstas:

- Dashboard: licenses activas, uso del mes, por vencer
- Clientes + notas de pago
- Crear/renovar licencia (plan, `valid_until`, max devices)
- Suspender / reactivar
- Devices + "kick seat"
- Planes: editar features/límites sin recompilar el add-in
- Releases: subir artefactos + publicar
- Audit log
