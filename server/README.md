# server/ -- License API

Backend de licencias descrito en [`docs/LICENSING_PLAN.md`](../docs/LICENSING_PLAN.md). Vive en
este mismo repo (monorepo) en vez de en uno aparte: comparte versión/documentación con el add-in y
es un solo desarrollador manteniendo ambos lados, así que un solo `git log` es más simple que
sincronizar dos repos. Su árbol de build es independiente del de `src/` -- `Directory.Build.props`
aquí fija `net10.0`, sin relación con la matriz `net48`/`net8.0-windows` de Revit.

## Proyectos

| Proyecto | Qué es | Pieza del plan |
| --- | --- | --- |
| `GvrLicense.Domain` | Entidades puras (`Customer`, `Plan`, `License`, `Device`, `UsageCounter`, `UsageEvent`, `Release`, `AuditLog`, `AppSettings`) | Pieza 1, "Modelo de datos" |
| `GvrLicense.Contracts` | DTOs de `/v1/*` (espejo mantenido a mano de `src/GvrTools.Licensing/Http/Dto`, porque el cliente corre en net48 y no puede referenciar este proyecto) | Pieza 1, "API mínima" |
| `GvrLicense.Infrastructure` | `LicenseDbContext` (EF Core + Npgsql) y las funciones/triggers SQL en `Sql/` | "Dónde vive la lógica: app vs Postgres" |
| `GvrLicense.Api` | ASP.NET Core: sirve `/v1/*` y `/admin/*` desde un solo contenedor | Pieza 1, 5 y 6 |
| `tests/GvrLicense.Api.Tests` | xUnit, mismo patrón que `tests/GvrTools.Core.Tests` | -- |

## Desarrollo local

```bash
docker compose up -d          # Postgres desechable en localhost:5432
dotnet build GvrLicense.slnx
```

La connection string local va en `appsettings.Development.json` (gitignored) -- nunca en
`appsettings.json`, que sí se versiona y solo trae placeholders vacíos.

### Arranque local rápido

```bash
cd server
docker compose up -d          # Postgres en localhost:5432
cd src/GvrLicense.Api
dotnet run                    # http://localhost:5299  (admin + /v1 + /download)
```

`appsettings.Development.json` debe incluir al menos:

- `ConnectionStrings:Postgres` (ver `docker-compose.yml`)
- `Signing:PrivateKeyPem` — generar con `dotnet run --project tools/GenerateSigningKey` y pegar el PEM (con `\n`). La pública va en `src/GvrTools.Licensing/Crypto/EmbeddedPublicKey.cs`.
- `Minio:*` (opcional para probar uploads; si falta, el admin de Releases avisa)

**Migrations:** en Development, `Program.cs` llama a `Database.Migrate()` al arrancar. En Production/EasyPanel no hay auto-migrate: aplica a mano con `dotnet ef database update` apuntando a `ConnectionStrings__Postgres`.

## Deploy

Guía de la **app** en EasyPanel (imagen, env, dominio):

→ **[`docs/DEPLOY_EASYPANEL.md`](../docs/DEPLOY_EASYPANEL.md)**

Postgres y MinIO se asumen ya desplegados; solo se pasan por env. La imagen Hub
`sistemasgvr/visor-gvr` es otro producto — usar `sistemasgvr/gvr-license-api`.

### Variables MinIO (EasyPanel)

| Env | Ejemplo |
| --- | --- |
| `Minio__Endpoint` | `https://sistemas-gvr-minio.odjkys.easypanel.host` (API, no la consola) |
| `Minio__AccessKey` | usuario MinIO |
| `Minio__SecretKey` | secret MinIO |
| `Minio__Bucket` | `gvr-tools-releases` |
| `Minio__PresignExpiryMinutes` | `60` (opcional) |

Consola MinIO (solo admin): https://console-sistemas-gvr-minio.odjkys.easypanel.host/browser/gvr-tools-releases

### Descarga del cliente

Tras publicar un release tipo **instalador** en `/Admin/Releases`:

- Landing de descarga: `https://<tu-dominio-license>/download` (HTML con `_PublicLayout`)
- Archivo `.exe` (redirect MinIO): `https://<tu-dominio-license>/download/file`
- Redirige a una URL firmada temporal del `.exe` en MinIO (bucket privado).

También: `ConnectionStrings__Postgres`, `Signing__PrivateKeyPem`.
