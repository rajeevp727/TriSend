# MVP Smoke Test

## 1. Start the API

```bash
dotnet run --project backend/TriSend.Api/TriSend.Api.csproj
```

## 2. Health

```bash
curl http://localhost:5000/health
```

Expected: HTTP 200.

## 3. Database health

```bash
curl http://localhost:5000/health/db
```

Expected: HTTP 200 with `"database":"postgres"`.

## 4. Send a test email

```bash
curl -X POST http://localhost:5000/v1/messages \
  -H "Authorization: Bearer <Mvp__ApiKey>" \
  -H "Content-Type: application/json" \
  -d '{"channel":"email","recipient":"delivered@resend.dev","subject":"TriSend MVP test","body":"Hello from TriSend","idempotencyKey":"trisend-smoke-test-001"}'
```

Expected: HTTP 200 with `status: "sent"` and a Resend provider message ID.

For a real recipient, configure a verified sender/domain in Resend and use `Resend__FromAddress` in the API environment.
