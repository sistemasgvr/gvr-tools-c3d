# Deploy: GVR TOOLS-RVT (License API) en EasyPanel

Cómo publicar **solo** el contenedor `GvrLicense.Api` en el proyecto EasyPanel **GVR TOOLS-RVT**
(admin + `/v1/*` + `/download`).

Postgres y MinIO **ya están** en el VPS: aquí solo se cablean por variables de entorno.

Relacionado: [`RUNBOOK_LICENSING.md`](RUNBOOK_LICENSING.md), [`server/README.md`](../server/README.md), Dockerfile en `server/Dockerfile`.

---

## Imagen correcta

| Imagen Hub | Uso |
| --- | --- |
| `sistemasgvr/visor-gvr` | **No** — es otro producto (Visor) |
| `sistemasgvr/gvr-license-api` | **Sí** — License API de este repo |

Puerto interno del contenedor: **8080**.

---

## 1. Clave de firma (una vez)

```powershell
cd server
dotnet run --project tools/GenerateSigningKey
```

- **Privada** → env `Signing__PrivateKeyPem` en EasyPanel (si el UI es una línea, usa `\n` entre líneas del PEM).
- **Pública** → `src/GvrTools.Licensing/Crypto/EmbeddedPublicKey.cs` y recompilar add-in/Setup.

No cambies la privada en prod sin actualizar el add-in.

---

## 2. Construir y subir la imagen

Docker Desktop logueado como `sistemasgvr`:

```powershell
cd server
docker build -t sistemasgvr/gvr-license-api:latest .
docker push sistemasgvr/gvr-license-api:latest
```

Opcional, tag versionado:

```powershell
docker tag sistemasgvr/gvr-license-api:latest sistemasgvr/gvr-license-api:1.0.0
docker push sistemasgvr/gvr-license-api:1.0.0
```

**Alternativa:** en EasyPanel, App con source **Git** y Dockerfile `server/Dockerfile` (contexto `server/`), sin pasar por Hub.

---

## 3. Crear el App en EasyPanel

Proyecto: **GVR TOOLS-RVT**.

1. Nuevo servicio **App** → imagen `sistemasgvr/gvr-license-api:latest` (o Git).
2. Puerto: **8080**.
3. Domains → p. ej. `license.tudominio.com` (DNS A/CNAME al VPS; HTTPS lo pone Traefik).

---

## 4. Environment (pegar lo que ya tenéis)

| Variable | Notas |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Postgres` | Host interno del Postgres **existente** |
| `Signing__PrivateKeyPem` | PEM del paso 1 |
| `Minio__Endpoint` | API del MinIO **existente** (no la consola) |
| `Minio__AccessKey` | Ya en uso |
| `Minio__SecretKey` | Ya en uso |
| `Minio__Bucket` | p. ej. `gvr-tools-releases` |
| `Minio__PresignExpiryMinutes` | Opcional (`60`) |

En Production el contenedor **no** auto-migra. Si la DB aún no tiene el esquema de este API:

```powershell
cd server
$env:ConnectionStrings__Postgres = "<mismo string de prod>"
dotnet ef database update --project src/GvrLicense.Infrastructure --startup-project src/GvrLicense.Api
```

Primer admin (si no hay usuarios):

```powershell
$env:ConnectionStrings__Postgres = "<mismo string>"
dotnet run --project tools/GenerateAdminBootstrap
```

---

## 5. Verificar

```text
https://<dominio>/health     → OK (habla con Postgres)
https://<dominio>/           → landing
https://<dominio>/admin      → login
https://<dominio>/download   → descarga (tras publicar un Instalador en Admin)
```

Add-in en prod: `%APPDATA%\GVR\GvrTools\license-config.json` → `"BaseUrl": "https://<dominio>"`.

Operación diaria: [`RUNBOOK_LICENSING.md`](RUNBOOK_LICENSING.md).

---

## 6. Actualizar la app

```powershell
cd server
docker build -t sistemasgvr/gvr-license-api:latest .
docker push sistemasgvr/gvr-license-api:latest
```

Redeploy en EasyPanel → comprobar `/health`.

Si el commit trae migraciones EF, `dotnet ef database update` antes o al redeploy.

El Setup `.exe` del add-in **no** va en esta imagen: se sube por Admin → Releases.

---

## Problemas frecuentes (app)

| Síntoma | Qué mirar |
| --- | --- |
| Crash al arrancar | `Signing__PrivateKeyPem` vacío o mal escapado |
| `/health` 500 | Connection string / red al Postgres interno |
| Admin sin usuario | `GenerateAdminBootstrap` |
| Firma inválida en Revit | Pública del add-in ≠ privada del env |
| Add-in apunta a local | `license-config.json` sigue en `localhost:5299` |
