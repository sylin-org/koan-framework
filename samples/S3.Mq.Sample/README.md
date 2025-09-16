# S3.Mq.Sample

A minimal end-to-end sample for Koan Messaging over RabbitMQ.

- Demonstrates default aliasing (full type name when no [Message] attribute).
- Demonstrates default group and auto-subscribe when no Subscriptions are configured.
- Uses OnMessage sugar for quick handler registration.

## Run

These commands are optional if you're not using integrated VS Code tasks.

- Build and start (detached):
  - docker compose -f samples/S3.Mq.Sample/compose/docker-compose.yml up -d --build
- Show logs:
  - docker compose -f samples/S3.Mq.Sample/compose/docker-compose.yml logs -f s3-mq-sample
- Stop:
  - docker compose -f samples/S3.Mq.Sample/compose/docker-compose.yml down

After startup you should see two handler lines similar to:

[handler] Hello -> Hello, Koan!
[handler] UserRegistered -> Welcome user u-1 (u1@example.com)

Batch example:

- The sample also sends a grouped batch of `UserRegistered` messages using `SendAsBatch()` and handles them with `OnBatch<UserRegistered>((env, batch, ct) => ...)`.
- Expected output includes lines like:
  - [batch] UserRegistered -> Saving 3 users...
  - [batch] -> u-2 (u2@example.com)
  - [batch] -> u-3 (u3@example.com)
  - [batch] -> u-4 (u4@example.com)

RabbitMQ management UI: http://localhost:15672 (guest/guest).
