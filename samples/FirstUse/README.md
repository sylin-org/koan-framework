# Koan First Use

This is Koan's executable first-use contract: one business entity, one controller, one bootstrap
call, SQLite persistence, runtime explanation, and an MCP projection over the same governed surface.

Read the application in this order:

1. [`Domain/Approval.cs`](Domain/Approval.cs) — the business model and its agent access policy.
2. [`Web/ApprovalsController.cs`](Web/ApprovalsController.cs) — the governed HTTP surface.
3. [`Program.cs`](Program.cs) — the complete host bootstrap.

That is the meaningful application. The project and settings express infrastructure intent; there
are no application repositories, database registrations, schema scripts, MCP tool handlers, or
health plumbing to maintain.

```powershell
dotnet run --project samples/FirstUse
```

Then create and read an approval:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/approvals -ContentType application/json -Body '{"subject":"Approve supplier invoice"}'
Invoke-RestMethod http://localhost:5000/api/approvals
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The exact URL is printed by ASP.NET Core and may differ when a launch profile or `--urls` is used.
HTTP MCP is enabled for local Development. Production keeps Koan's authentication-required posture.

When it is working well:

- startup identifies the composed modules and elected adapter;
- `/.well-known/Koan/facts` explains the same runtime decisions to an operator;
- `koan://facts` gives an MCP client the identical redacted envelope;
- `koan://entities` offers the agent the operations its origin is allowed to use;
- the remote agent can dry-run or upsert an approval, but cannot discover the local-only delete;
- a write made through MCP is immediately observable through the REST API.

The release compiler copies this same directory outside the repository and rebuilds it exclusively
from locally staged `Sylin.Koan.*` packages. `FirstUseContractTests` proves the source lane; the
release clean room writes `first-use-package-evidence.json` for the package lane. Public package
availability is a separate release fact.
