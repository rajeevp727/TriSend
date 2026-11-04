# API Contract

Base URL:

`https://{api-host}/v1`

## Authentication

Machine clients send:

`Authorization: Bearer <tenant-api-key>`

Never send API keys in query strings.

## Send message

### POST /v1/messages

Example:

```json
{
  "channel": "email",
  "recipient": "customer@example.com",
  "subject": "Order update",
  "body": "Your order has shipped.",
  "idempotencyKey": "order-123-shipped"
}
```

Current MVP supports `email` only. SMS and WhatsApp requests are rejected until their providers are implemented.

Successful response:

```json
{
  "id": "message-id",
  "status": "sent",
  "channel": "email",
  "providerMessageId": "resend-email-id"
}
```

Expected HTTP status: `200 OK`.

Provider failure returns HTTP `502 Bad Gateway` and the message is stored with status `failed`.

## Get message

### GET /v1/messages/{id}

Returns the stored message status, recipient, channel, provider message ID and timestamps.

## Health

### GET /health

Returns HTTP 200 when the API process is healthy.

### GET /health/db

Returns HTTP 200 when the API can connect to Supabase PostgreSQL.

## Error format

```json
{
  "code": "validation_error",
  "message": "Recipient and body are required."
}
```
## Application password authentication

The shared authentication API supports email/password registration and login for registered applications.

### POST /auth/register

```json
{
  "email": "driver@example.com",
  "password": "UseAUniquePassword123",
  "name": "Example Driver",
  "appId": "turnova",
  "role": "Driver",
  "replaceOldest": false
}
```

### POST /auth/login

```json
{
  "email": "driver@example.com",
  "password": "UseAUniquePassword123",
  "appId": "turnova",
  "replaceOldest": false
}
```

Both endpoints return an access token, refresh token, session ID, expiry, and user details. Access tokens are short-lived; refresh tokens are rotated when `POST /auth/refresh` succeeds.

### POST /auth/refresh

```json
{
  "refreshToken": "<refresh-token>"
}
```

### Session endpoints

- `POST /auth/logout` — revoke the current session. Requires `Authorization: Bearer <access-token>`.
- `POST /auth/logout-all` — revoke all sessions for the authenticated user.
- `GET /auth/me` — return the authenticated user's identity and application role.
- `GET /auth/sessions` — list active sessions.
- `DELETE /auth/sessions/{sessionId}` — revoke one session.

The service allows up to three active sessions per user. When the limit is reached, registration/login returns `409` with `maxSessions: true`; the client may retry with `replaceOldest: true` after explicit user confirmation.

Supported application roles are `JobSeeker`, `Employer`, `Driver`, and `Rider`. Existing JobSeeker/Employer values remain unchanged. Applications should use only the roles defined by their own product contract.

The password endpoints do not require an OAuth redirect or provider configuration. OAuth endpoints are separate and optional.
