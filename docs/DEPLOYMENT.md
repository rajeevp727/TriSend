# TriSend Deployment

## Current architecture

- Render Web Service: TriSend API
- Supabase: PostgreSQL database
- Resend: transactional email provider
- React frontend: separate client application

There is no worker service in the current MVP. The API sends email directly to Resend over HTTPS.

## Secrets

Set these as Render environment variables; never commit them.

### API

- `Mvp__ApiKey`
- `Mvp__TenantId`
- `ConnectionStrings__Postgres`
- `Resend__ApiKey`
- `Resend__FromAddress`
- `Resend__FromName`

The Resend API key must be a server-side key with sending permission. Keep it only in Render's environment configuration.

## Database migration

Apply `database/migrations/001_initial_schema.sql` in the Supabase SQL editor.

Database objects are created only through SQL migration files. Do not create application tables manually.

## Render API

- Service type: Web Service
- Language: Docker
- Branch: `main`
- Root Directory: blank
- Dockerfile Path: `Dockerfile`
- Docker Context: repository root
- Health check path: `/health`
- Container port: `10000`

## Deployment order

1. Apply the Supabase migration.
2. Configure the Render API service.
3. Add the API and Resend environment variables.
4. Deploy from `main`.
5. Verify `/health` and `/health/db`.
6. Run the email smoke test.

The repository no longer contains the old Azure deployment, Azure infrastructure, or background-worker implementation.
