# Sylin.Koan.Mcp.Operations

Governed MCP control-plane verbs for Koan Jobs and Cache. The package adds a small operational vocabulary while
reusing MCP discovery/execution, the durable Jobs ledger, Cache policy/tags, `AgentGrant`, and `AgentAction` audit.

## Install and enable

```powershell
dotnet add package Sylin.Koan.Mcp.Operations
```

Keep the normal `AddKoan()` host. Operational toolsets default off—even in Development—and are enabled explicitly:

```json
{
  "Koan": {
    "Mcp": {
      "Operations": {
        "Jobs": true,
        "Cache": true
      }
    }
  }
}
```

Enablement makes tools available; it does not authorize a caller. Give a governed agent the exact resource grant:

```csharp
await new AgentGrant
{
    Subject = agentSubject,
    Resource = "@ops:jobs"
}.Save();
```

Cache operations require `@ops:cache`. A blanket Entity grant does not confer operational authority.

## Meaningful result

| Tool | Result and guard |
|---|---|
| `koan.jobs.trigger` | Trigger a registered job action; exact Jobs grant; mutation audited. |
| `koan.jobs.cancel` | Cancel active work by type/id; exact Jobs grant; `confirm:true`; mutation audited. |
| `koan.jobs.status` | Read latest work status; exact Jobs grant. |
| `koan.cache.flush` | Flush entries tagged for one Entity type name; exact Cache grant; mutation audited. |
| `koan.cache.flushAll` | Flush every registered cacheable Entity tag; exact Cache grant; `confirm:true`; mutation audited. |

Without `confirm:true`, destructive tools return a dry-run description and make no change. Startup reporting names
the available and enabled toolsets plus the grant/confirmation posture.

## Guarantees and boundaries

- The package reference adds Jobs, Cache, and MCP dependencies, but tools remain absent until their toolset flag is
  enabled. This layered activation is deliberate because the surface is operational and privileged.
- Every invocation requires an authenticated subject with an active exact `AgentGrant`. Anonymous local STDIO calls
  cannot hold an operational grant and fail with a corrective message.
- Mutating successful operations write `AgentAction`; a failed or dry-run operation is not recorded as a completed
  mutation. Audit persistence follows the configured Entity data guarantee.
- Confirmation prevents accidental direct invocation; it is not multi-party approval, transaction rollback, or an
  exactly-once guarantee.
- `flushAll` enumerates registered cache policy tags. It is not a provider-native physical database purge and does
  not reach cache entries outside Koan's registered tag vocabulary.
- Jobs trigger/cancel semantics remain those of `IJobCoordinator`; this package does not add scheduling, payload
  upload, arbitrary code execution, or a general administrative shell.

See [TECHNICAL.md](TECHNICAL.md) for gate, audit, and discovery ownership.
