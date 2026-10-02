# Consuming TriSend Messaging

TriSend is an API-first communications platform. Applications consume the messaging API rather than integrating directly with individual delivery providers.

The current platform also provides centralized authentication through TriSend Identity. Authentication and messaging are separate concerns.

## Integration model

~~~text
GreenPantry / SprintDeck / Any App
             |
       OIDC login via TriSend Identity
             |
             v
        Application API
             |
             | HTTPS + bearer access token
             v
        TriSend Messaging API
             |
       SMS / WhatsApp / Email providers
~~~

## Authentication

Applications should use TriSend Identity for user authentication where they are registered as OIDC clients.

- Browser applications: Authorization Code + PKCE, public client.
- Server-side applications: confidential client where appropriate.
- APIs: validate TriSend-issued JWTs and the API-specific audience/resource.

See [identity/README.md](./identity/README.md) and [identity/dotnet-api.md](./identity/dotnet-api.md).

## Messaging REST API

The messaging API remains the canonical application-to-TriSend messaging contract.

Base URL and deployment-specific authentication details should come from the active environment configuration. Do not hard-code production endpoints or secrets in documentation examples.

Example:

~~~http
POST /v1/messages
Authorization: Bearer <application-token>
Content-Type: application/json

{
  "channel": "sms",
  "recipient": "+919876543210",
  "body": "Your OTP is 123456",
  "idempotencyKey": "otp-login-123456"
}
~~~

For WhatsApp or email, use the corresponding channel and message fields defined by [API.md](./API.md).

## Provider isolation

Consumer applications must not need to know provider credentials for SMS, WhatsApp or email.

Provider credentials, retries, delivery status and provider-specific behavior remain behind the TriSend messaging API boundary.

## Application authorization

Authentication through TriSend Identity does not replace application authorization.

Each consuming application remains responsible for its own roles, permissions, tenant/business rules and access checks. A valid identity token is not sufficient to authorize every operation.

## API key migration note

Older documentation referred to a generic Project SendGrid API-key integration. That is no longer the identity architecture for the platform. New application integrations should use the centralized TriSend Identity model where an authenticated application/user flow is required, while messaging-specific machine credentials should follow the active messaging API contract.
