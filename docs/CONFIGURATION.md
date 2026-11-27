# Configuration

## Local

Copy `backend/Communication.Api/appsettings.example.json` to a local configuration file or use environment variables.

Important variables:

- `Mvp__ApiKey`
- `Mvp__TenantId`
- `ConnectionStrings__Sql`

## Azure

Use App Service application settings and managed identity/Key Vault references.

Never commit:

- API keys
- provider tokens
- WhatsApp secrets
- SMTP credentials
- database passwords
- Service Bus connection strings

## Provider configuration

Provider-specific credentials should eventually be represented by ProviderAccount records and loaded from Key Vault at runtime.


## Production CORS

Set `Cors:AllowedOrigins` to the exact HTTPS frontend origins (for example, the BullionIQ production origin). Production startup fails when the allow-list is empty. Wildcard CORS is intentionally disabled. Add OAuth frontend callback URLs separately to `Auth:AllowedRedirectUris`; these are exact redirect URIs and are not interchangeable with CORS origins.
