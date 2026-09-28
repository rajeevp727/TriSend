# TriSend

An API-first communications platform for **email**, with SMS and WhatsApp reserved for future provider integrations.

## Current MVP architecture

```
React / client -> Render API -> Supabase PostgreSQL
                         |
                         -> Resend Email API
```

The API sends email directly to Resend over HTTPS. There is no SMTP dependency and no background worker in the current MVP.

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
  "body": "Your order has shipped.",
  "idempotencyKey": "order-123-shipped"
}
```

The API stores the message in Supabase, sends it through Resend, and records the provider message ID and final status.

## Database

Supabase PostgreSQL is the source of truth. Database changes are SQL migrations under `database/migrations/`.

Apply migrations in the Supabase SQL editor. Do not create application tables manually.

## Deployment

The API is a Docker service built from the repository root:

- Dockerfile: `Dockerfile`

Render auto-deploys the API from the `main` branch.

## Documentation

- [API](docs/API.md)
- [MVP](docs/MVP.md)
- [Architecture](docs/architecture.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Local development](docs/LOCAL-DEVELOPMENT.md)
- [Smoke test](docs/SMOKE-TEST.md)
