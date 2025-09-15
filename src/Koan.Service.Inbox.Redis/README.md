Koan-service-inbox-redis

- Purpose: HTTP Inbox service backed by Redis.
- Endpoints:
  - GET /v1/inbox/{key}
  - POST /v1/inbox/mark-processed
- Config:
  - Koan:Inbox:Redis:ConnectionString or ConnectionStrings:InboxRedis
  - PORT/ASPNETCORE_URLS for HTTP port (default 8080)
  - Optional RabbitMQ for discovery announce (ADR-0026):
    - Koan:Messaging:Buses:rabbit:ConnectionString (amqp URI)
    - Koan:Messaging:Buses:rabbit:RabbitMq:Exchange (default "Koan")
    - Koan:Messaging:DefaultBus (default "rabbit")
    - Koan:Messaging:DefaultGroup (default "workers")

Discovery announce
- On startup, if a RabbitMQ connection string is provided, the service listens on `Koan.discovery.ping.{bus}.{group}` and replies with `{ endpoint: "http://host:port" }` to the `reply-to` queue with the same correlationId. This enables auto-wiring of `HttpInboxStore` clients in dev/compose per ADR-0026.
