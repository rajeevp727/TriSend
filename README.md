# TriSend

An API-first communications platform for **SMS, WhatsApp and email**.

## Current MVP architecture

```
React / client -> Render API -> Supabase PostgreSQL
                         |
                         -> Render Background Worker -> SMTP
```

The MVP currently sends **email** through SMTP. SMS and WhatsApp remain part of the API contract but are rejected by the worker until their providers are implemented.

## Send a message

```http
POST /v1/messages
Authorization: Bearer <API_KEY>
Content-Type: application/json
```

```json
{
  "channel": "email",
  "recipient": "customer@example.com",
  "subject": "Order update",
  "body": "Your order has shipped."
}
```

The API returns HTTP 202 with a queued message ID. The Render worker claims queued messages from PostgreSQL and updates the message status.

## Database

Supabase PostgreSQL is the source of truth. Database changes are SQL migrations under `database/migrations/`.

Apply migrations in the Supabase SQL editor. Do not create application tables manually.

## Deployment

The API and worker are Docker services built from the repository root context:

- API Dockerfile: `backend/TriSend.Api/Dockerfile`
- Worker Dockerfile: `workers/TriSend.Worker/Dockerfile`

Render can auto-deploy both services from the `main` branch.

## Documentation

- [API](docs/API.md)
- [MVP](docs/MVP.md)
- [Architecture](docs/architecture.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Local development](docs/LOCAL-DEVELOPMENT.md)
- [Smoke test](docs/SMOKE-TEST.md)
