# Local Development

## Prerequisites

- .NET 10 SDK
- Node.js 22+
- Git

## Backend

```bash
dotnet run --project backend/TriSend.Api/TriSend.Api.csproj
```

Health check:

```text
GET http://localhost:5000/health
```

The API requires PostgreSQL and Resend configuration through local environment variables or user-secrets.

## Frontend

```bash
cd frontend
npm install
npm run dev
```

Set the API URL through the Vite environment configuration.

## Local secrets

Use user-secrets or local environment variables. Never commit database credentials, API keys, or provider credentials.
