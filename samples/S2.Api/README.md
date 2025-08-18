# S2.Api (Mongo-backed API)

Quickstart:
- Dev run:
  - ConnectionStrings__Default: mongodb connection string
  - Sora__Data__Mongo__Database: database name
- Defaults in start.bat:
  - ASPNETCORE_URLS: http://localhost:5054
  - ConnectionStrings__Default: mongodb://localhost:5055 (matches Testcontainers and Compose assumptions)

Endpoints:
- GET /api/health
- GET /api/items
- POST /api/items
- DELETE /api/items/{id}
 - POST /api/items/seed/{count}
 - DELETE /api/items/clear

Compose:
- See ../S2.Compose for a docker-compose stack with mongo + api + an AngularJS client (plus a simple probe container).
