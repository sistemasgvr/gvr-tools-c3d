-- docs/LICENSING_PLAN.md, "Dónde vive la lógica: app (EF Core) vs Postgres (función/trigger)".
--
-- UPDATE...RETURNING atómico: evita la carrera entre dos reportes de uso casi simultáneos (dos
-- devices del mismo seat, o un reintento de red) sin locks explícitos en C#. Si la fila no cumple
-- la condición (se pasaría del límite), no actualiza nada y devuelve NULL -- el llamador debe
-- interpretar NULL como "bloquear, no alcanza la cuota". quota_limit = -1 significa ilimitado y se
-- excluye de la condición.

create or replace function consume_quota(
    p_license_id uuid,
    p_feature text,
    p_amount int
) returns int as $$
    update usage_counter
    set consumed = consumed + p_amount
    where license_id = p_license_id
      and feature_code = p_feature
      and period = date_trunc('month', now() at time zone 'utc')::date
      and (quota_limit = -1 or consumed + p_amount <= quota_limit)
    returning case when quota_limit = -1 then -1 else quota_limit - consumed end;
$$ language sql;
