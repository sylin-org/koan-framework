# S15 Redis Inbox Sample

This sample demonstrates hosting the `Koan.Service.Inbox.Connector.Redis` module inside a minimal ASP.NET Core application. The inbox module self-registers via `KoanAutoRegistrar`, so the Program only needs to call `AddKoan().AsWebApi()`.

## Running

1. Ensure Redis and RabbitMQ are available (for example via `docker compose`).
2. From the repository root, run:

```pwsh
pwsh ./scripts/cli-run.ps1 S15.RedisInbox
```

The service will expose the inbox HTTP routes and respond to Koan messaging discovery pings.
