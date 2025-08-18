sora-service-inbox-redis

- Purpose: HTTP Inbox service backed by Redis.
- Endpoints:
  - GET /v1/inbox/{key}
  - POST /v1/inbox/mark-processed
- Config:
  - Sora:Inbox:Redis:ConnectionString or ConnectionStrings:InboxRedis
  - PORT/ASPNETCORE_URLS for HTTP port (default 8080)
  - Optional RabbitMQ for discovery announce (ADR-0026):
    - Sora:Messaging:Buses:rabbit:ConnectionString (amqp URI)
    - Sora:Messaging:Buses:rabbit:RabbitMq:Exchange (default "sora")
    - Sora:Messaging:DefaultBus (default "rabbit")
    - Sora:Messaging:DefaultGroup (default "workers")

Discovery announce
- On startup, if a RabbitMQ connection string is provided, the service listens on `sora.discovery.ping.{bus}.{group}` and replies with `{ endpoint: "http://host:port" }` to the `reply-to` queue with the same correlationId. This enables auto-wiring of `HttpInboxStore` clients in dev/compose per ADR-0026.
