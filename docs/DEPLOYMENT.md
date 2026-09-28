# TriSend Deployment

## Infrastructure split

### Application code

Managed in GitHub and deployed automatically by Render from `main`:

- Render Web Service: TriSend API
- Render Background Worker: TriSend Worker

### Database

Supabase PostgreSQL.

Database objects are created only through SQL migration files in `database/migrations/`.

### Secrets

Set these as Render environment variables; never commit them:

#### API

- `Mvp__ApiKey`
- `Mvp__TenantId`
- `ConnectionStrings__Postgres`

#### Worker

- `ConnectionStrings__Postgres`
- `Smtp__Host`
- `Smtp__Port`
- `Smtp__Username`
- `Smtp__Password`
- `Smtp__FromAddress`
- `Smtp__FromName`
- `Smtp__EnableSsl`

## Database migration

Apply `database/migrations/001_initial_schema.sql` in the Supabase SQL editor.

The migration creates the tenants, messages, and message-events tables and inserts the MVP tenant used by the default configuration.

## Render API

- Language: Docker
- Branch: `main`
- Root Directory: blank
- Dockerfile Path: `backend/TriSend.Api/Dockerfile`
- Docker Context: `.`
- Health check path: `/health`
- The container listens on port 10000.

## Render worker

- Service type: Background Worker
- Language: Docker
- Branch: `main`
- Root Directory: blank
- Dockerfile Path: `workers/TriSend.Worker/Dockerfile`
- Docker Context: `.`

No inbound port is required for the worker.

## Deployment order

1. Apply the Supabase migration.
2. Create/configure the Render API service.
3. Create/configure the Render worker.
4. Add the environment variables and secrets.
5. Deploy both services from `main`.
6. Run the smoke test.

The repository no longer contains an Azure deployment workflow for the API/worker.
