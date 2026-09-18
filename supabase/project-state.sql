-- ProjectHub v0.2: minimum project and project-state storage.
-- Run this script in the Supabase SQL Editor after workstations.sql.

create table if not exists public.projects (
    id uuid primary key default gen_random_uuid(),
    project_id text not null unique,
    display_name text not null,
    repository_url text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists public.project_states (
    id uuid primary key default gen_random_uuid(),
    project_id text not null references public.projects(project_id) on delete cascade,
    workstation_id text not null references public.workstations(workstation_id) on delete cascade,
    branch text,
    head_sha text,
    dirty boolean not null default false,
    changed_count integer not null default 0,
    untracked_count integer not null default 0,
    deleted_count integer not null default 0,
    diff_fingerprint text,
    last_file_activity timestamptz,
    last_seen timestamptz not null default now(),
    state_json jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique(project_id, workstation_id)
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

drop trigger if exists projects_set_updated_at on public.projects;
create trigger projects_set_updated_at
before update on public.projects
for each row
execute function public.projecthub_set_updated_at();

drop trigger if exists project_states_set_updated_at on public.project_states;
create trigger project_states_set_updated_at
before update on public.project_states
for each row
execute function public.projecthub_set_updated_at();

alter table public.projects enable row level security;
alter table public.project_states enable row level security;
