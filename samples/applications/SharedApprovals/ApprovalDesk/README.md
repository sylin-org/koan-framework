# Approval desk

Submit a supplier purchase, approve it within the shared spending limit, then place its order.

```powershell
dotnet run --project samples/applications/SharedApprovals/ApprovalDesk -- --urls http://127.0.0.1:5101
```

Open the loopback URL in a browser. The application stores purchases in its own SQLite file.
`appsettings.json` owns connection intent; an environment override uses
`Koan__Data__Sources__Default__ConnectionString`.

Read `Domain/PurchaseRequest.cs`, `Initialization/PurchaseModule.cs`, and
`Web/PurchasesController.cs`. The foundation owns approval; purchasing owns supplier, cost center,
and the final purchase-order reference. The order action records an internal reference and does
not place an order with an external supplier.

HTTP CRUD lives at `/api/purchases`, with `POST /api/purchases/{id}/approve` and
`POST /api/purchases/{id}/order` for the business actions. Inspect `/.well-known/Koan/facts`
and `/health/ready` for composition and dependencies. Local Development also enables MCP over
the same Entity; use the workspace verification before making cross-surface claims.

This is the A03 flagship application's baseline. Identity, tenancy, search, background work,
and the recording have separate work items. Read the [workspace guide](../README.md) for the
versioned foundation experiment and the demonstration's operating limits.

Coding agents: read the workspace [AGENTS.md](../AGENTS.md) before extending this application.
