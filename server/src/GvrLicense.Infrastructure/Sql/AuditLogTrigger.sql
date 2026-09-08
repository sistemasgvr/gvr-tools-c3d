-- docs/LICENSING_PLAN.md, "Dónde vive la lógica: app (EF Core) vs Postgres (función/trigger)".
--
-- Garantiza el rastro de auditoría aunque alguien edite License.status directo en psql, o un
-- endpoint futuro olvide llamar al audit manualmente -- no depende de que el código de app se
-- acuerde de hacerlo.

create or replace function audit_license_status_change() returns trigger as $$
begin
    if new.status is distinct from old.status then
        insert into audit_log (id, license_id, actor, action, details_json, occurred_at_utc)
        values (
            gen_random_uuid(),
            new.id,
            coalesce(current_setting('gvr.actor', true), 'system'),
            'license_status_changed',
            jsonb_build_object('from', old.status, 'to', new.status),
            now()
        );
    end if;
    return new;
end;
$$ language plpgsql;

drop trigger if exists trg_audit_license_status on license;

create trigger trg_audit_license_status
    after update on license
    for each row
    execute function audit_license_status_change();
