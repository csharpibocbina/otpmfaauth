-- Run via Supabase CLI (`supabase db push`) or the SQL editor.
-- Table/column names match EFCore.NamingConventions (snake_case).

create table if not exists public.users (
  id uuid primary key,
  email text not null,
  password_hash text not null,
  phone_e164 text,
  phone_verified_at timestamptz,
  mfa_enabled boolean not null default false,
  failed_login_count int not null default 0,
  locked_until timestamptz,
  created_at timestamptz not null,
  updated_at timestamptz not null
);
create unique index if not exists ix_users_email on public.users (email);

create table if not exists public.verification_attempts (
  id uuid primary key,
  user_id uuid not null references public.users (id) on delete cascade,
  channel text not null,
  destination_hash text not null,
  vonage_request_id text,
  status text not null,
  check_count int not null default 0,
  expires_at timestamptz not null,
  created_at timestamptz not null,
  updated_at timestamptz not null
);
create index if not exists ix_verification_attempts_user_created
  on public.verification_attempts (user_id, created_at desc);

-- Only the API (service/postgres connection) touches these tables.
-- RLS enabled with no policies => PostgREST roles (anon/authenticated) get no access.
alter table public.users enable row level security;
alter table public.verification_attempts enable row level security;
