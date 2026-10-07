create table if not exists auth_users (
    id uuid primary key,
    email varchar(320) not null unique,
    display_name varchar(200),
    avatar_url text,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create table if not exists auth_identities (
    id uuid primary key,
    user_id uuid not null references auth_users(id) on delete cascade,
    provider varchar(30) not null,
    provider_subject varchar(320) not null,
    email varchar(320),
    created_at_utc timestamptz not null default now(),
    unique(provider, provider_subject)
);

create table if not exists auth_sessions (
    id uuid primary key,
    user_id uuid not null references auth_users(id) on delete cascade,
    app_id varchar(100) not null,
    role varchar(50) not null,
    refresh_token_hash varchar(128) not null unique,
    created_at_utc timestamptz not null default now(),
    last_used_at_utc timestamptz not null default now(),
    expires_at_utc timestamptz not null,
    revoked_at_utc timestamptz,
    user_agent text,
    ip_address varchar(100)
);

create index if not exists ix_auth_sessions_user_active
    on auth_sessions(user_id, revoked_at_utc, expires_at_utc);

create table if not exists auth_requests (
    state varchar(128) primary key,
    provider varchar(30) not null,
    role varchar(50) not null,
    app_id varchar(100) not null,
    redirect_uri text not null,
    code_verifier varchar(256) not null,
    expires_at_utc timestamptz not null
);

create table if not exists auth_login_tickets (
    code varchar(128) primary key,
    user_id uuid not null references auth_users(id) on delete cascade,
    app_id varchar(100) not null,
    role varchar(50) not null,
    expires_at_utc timestamptz not null,
    consumed_at_utc timestamptz
);

create index if not exists ix_auth_login_tickets_expiry
    on auth_login_tickets(expires_at_utc);