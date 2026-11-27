create table if not exists auth_email_otps (
 id uuid primary key, app_id varchar(100) not null, email varchar(320) not null,
 code_hash varchar(64) not null, attempt_count integer not null default 0 check(attempt_count>=0),
 created_at_utc timestamptz not null default now(), expires_at_utc timestamptz not null,
 consumed_at_utc timestamptz, request_ip varchar(100)
);
create index if not exists ix_auth_email_otps_rate_limit on auth_email_otps(app_id,email,created_at_utc desc);
create index if not exists ix_auth_email_otps_active on auth_email_otps(app_id,email,expires_at_utc) where consumed_at_utc is null;
