-- ProjectHub v0.2: minimum workstation state.
-- Run this script in the Supabase SQL Editor.

create table if not exists public.workstations (
    id uuid primary key default gen_random_uuid(),
    workstation_id text not null unique,
    display_name text not null,
    hostname text,
    last_seen timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create or replace function public.projecthub_set_updated_at()
returns trigger
language plpgsql
as $$
begin
    new.updated_at = now();
    return new;
end;
$$;

drop trigger if exists workstations_set_updated_at on public.workstations;

create trigger workstations_set_updated_at
before update on public.workstations
for each row
execute function public.projecthub_set_updated_at();

alter table public.workstations enable row level security;
