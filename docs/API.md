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
