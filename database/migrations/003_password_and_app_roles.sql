create table if not exists auth_password_credentials (
    user_id uuid primary key references auth_users(id) on delete cascade,
    password_hash varchar(128) not null,
    password_salt varchar(128) not null,
    iterations integer not null default 120000,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now()
);

create table if not exists auth_user_roles (
    user_id uuid not null references auth_users(id) on delete cascade,
    app_id varchar(100) not null,
    role varchar(50) not null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    primary key(user_id, app_id)
);

create index if not exists ix_auth_user_roles_app_role
    on auth_user_roles(app_id, role);
