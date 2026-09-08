# Sql/

Funciones y triggers de Postgres que EF Core no puede expresar en Fluent API (ver
docs/LICENSING_PLAN.md, "Dónde vive la lógica: app (EF Core) vs Postgres (función/trigger)").

Se aplican como SQL crudo pegado literal dentro de `Migrations/20260824225756_InitialCreate.cs`
(`migrationBuilder.Sql(...)`) -- no se leen de disco en tiempo de aplicar la migración, para no
depender de la carpeta de trabajo dentro del contenedor. Estos `.sql` son la fuente de verdad
legible; si se editan, hay que copiar el cambio a mano dentro de la migración (o generar una
migración nueva) y correr `dotnet ef database update`.

- `ConsumeQuota.sql` -- consumo atómico de cuota mensual, llamado desde `POST /v1/usage`.
- `AuditLogTrigger.sql` -- inserta en `audit_log` automáticamente cuando cambia `License.Status`.
