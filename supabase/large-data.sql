create table if not exists public.large_objects (
  sha256 text primary key check (sha256 ~ '^[0-9a-f]{64}$'),
  size_bytes bigint not null check (size_bytes >= 0),
  lifecycle text not null check (lifecycle in ('LOCAL_ONLY','HASHING','UPLOADING','STAGED','CHECKPOINTED','ORPHANED','MISSING','MIGRATION_REQUIRED')),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.large_upload_sessions (
  id uuid primary key default gen_random_uuid(),
  project_id text not null,
  workstation_id text not null,
  sha256 text not null references public.large_objects(sha256),
  size_bytes bigint not null check (size_bytes >= 0),
  chunk_size_bytes bigint not null check (chunk_size_bytes between 16777216 and 67108864),
  storage_scope text not null,
  lifecycle text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.project_large_files (
  project_id text not null,
  relative_path text not null,
  sha256 text not null references public.large_objects(sha256),
  size_bytes bigint not null check (size_bytes >= 0),
  lifecycle text not null,
  checkpoint_commit_sha text,
  updated_at timestamptz not null default now(),
  primary key (project_id, relative_path)
);

create table if not exists public.large_data_sets (
  id uuid primary key default gen_random_uuid(),
  project_id text not null,
  commit_sha text not null,
  status text not null check (status in ('STAGED','CHECKPOINTED')),
  created_at timestamptz not null default now()
);

create table if not exists public.large_data_set_items (
  data_set_id uuid not null references public.large_data_sets(id) on delete cascade,
  sha256 text not null references public.large_objects(sha256),
  relative_path text not null,
  size_bytes bigint not null check (size_bytes >= 0),
  primary key (data_set_id, relative_path)
);
