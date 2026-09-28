# MVP Definition

## Product goal

TriSend is a developer-first messaging API that currently sends transactional email through Resend and stores message state in Supabase PostgreSQL.

## Current MVP

- Tenant-scoped API key authentication
- Email send API
- Resend provider integration
- PostgreSQL persistence
- Message status tracking
- Provider message ID tracking
- Health and database health endpoints
- React dashboard foundation
- Docker deployment on Render

## Current limitations

- SMS is not implemented.
- WhatsApp is not implemented.
- Provider webhooks are not implemented.
- Message listing/pagination is not implemented.
- Advanced tenant management and billing are not implemented.

## Message lifecycle

1. Client sends an authenticated email request.
2. API validates the request.
3. API stores the message as `processing`.
4. API calls Resend.
5. API stores `sent` with the Resend email ID, or `failed` with the provider error.
6. Client can retrieve the stored message by ID.

Database changes are managed only through SQL migrations in `database/migrations/`.
