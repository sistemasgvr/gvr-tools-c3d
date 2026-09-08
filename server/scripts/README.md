# Scripts SQL operativos (License API)

Scripts pensados para **revisión humana** antes de tocar Postgres de producción.
No se ejecutan solos desde CI ni desde el arranque del API.

| Script | Qué hace |
| --- | --- |
| [`cleanup-licenses-clients.sql`](cleanup-licenses-clients.sql) | Borra licencias, devices, usage, company_users y clientes; **elimina** el plan `trial`; **conserva** `admin_user`, planes free/starter/pro, releases, settings, audit_log y la cáscara **"GVR Free installs"** |

## Decisión Free vs Trial

| Plan | Código | Estado |
| --- | --- | --- |
| **Free** (freemium) | `free` | **Activo** — `POST /v1/activate-free`; display name `Free` |
| **Trial 14 días** | `trial` | **Eliminado** de la BD (no hay fila). Freemium = solo Free |

Si en el futuro necesitás “trial de pago”, emití Starter/Pro con `valid_until` corto; no hace falta un plan `trial` aparte.

## Producción (EasyPanel) — pasos seguros

1. **Backup** del servicio Postgres en EasyPanel (pestaña Backups) o `pg_dump` manual.
2. Abrí una sesión SQL contra el Postgres **del proyecto GVR TOOLS-RVT** (consola EasyPanel del servicio Postgres, o `psql` vía SSH/túnel con el mismo host/user/db de `ConnectionStrings__Postgres`).
3. Pegá / ejecutá **solo** el contenido de `cleanup-licenses-clients.sql` tras leerlo.
4. Confirmá los `NOTICE` BEFORE/AFTER: `admin_user` > 0, `license` = 0, customer ≥ 1 (`GVR Free installs`), `plan.trial.count` = 0.
5. Reiniciá (opcional) el contenedor `gvr-license-api` — el seed de `Program.cs` asegura `free` activo y **borra** `trial` si no tiene licencias.
6. En Admin → Planes: solo `Free`, `Starter`, `Pro` (sin Trial).

**No** borrar `admin_user`. **No** `DROP TABLE`. **No** borrar el plan `free`.

## Local (dev)

```powershell
cd server
docker compose up -d
Get-Content .\scripts\cleanup-licenses-clients.sql -Raw |
  docker compose exec -T postgres psql -U gvrlicense_dev -d gvrlicense_dev
```

Solo contra el Postgres de `docker-compose.yml` (puerto 5432 local). Nunca apuntes ese comando a prod.
