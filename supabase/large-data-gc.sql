-- Apply once after large-data.sql. This migration only extends upload-session metadata.
alter table public.large_upload_sessions
  add column if not exists last_activity_at timestamptz not null default now();

update public.large_upload_sessions
set last_activity_at = coalesce(last_activity_at, updated_at);

do $$
declare constraint_name text;
begin
  select conname into constraint_name
  from pg_constraint
  where conrelid = 'public.large_upload_sessions'::regclass
    and contype = 'c'
    and pg_get_constraintdef(oid) like '%lifecycle%';
  if constraint_name is not null then
    execute format('alter table public.large_upload_sessions drop constraint %I', constraint_name);
  end if;
end $$;

alter table public.large_upload_sessions
  add constraint large_upload_sessions_lifecycle_check
  check (lifecycle in ('UPLOADING','COMPLETED','CANCELLED','ABANDONED','STAGED'));
