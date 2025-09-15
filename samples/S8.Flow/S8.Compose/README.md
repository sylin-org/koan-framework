# S8 Compose (Flow API + Mongo + RabbitMQ + Adapters)

Prereqs: Docker Desktop
Start the stack (container-only sample):

```powershell
cd samples/S8.Compose
# Build images and run services
docke compose up --build
```

Services:
- MongoDB: localhost:4900 -> container 27017
- RabbitMQ: AMQP at localhost:4901 -> 5672; Management UI at http://localhost:4902 -> 15672 (guest/guest)
- S8 Flow API: http://localhost:4903
  - Health: GET /api/health
  - Views: GET /views/canonical, /views/lineage (from Koan.Flow.Web)
  - Static monitor: http://localhost:4903/

Notes:
- API uses Koan.Data.Mongo in containers (no filesystem JSON store).
- Two adapter containers publish TelemetryEvent messages via RabbitMQ; the API consumes and persists intake for Flow processing.
- Stop stack: docker compose down