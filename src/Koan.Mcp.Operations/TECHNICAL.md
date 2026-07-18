# Sylin.Koan.Mcp.Operations — technical contract

## Composition ownership

`McpOperationsModule` owns only package reporting. MCP Core discovers the package's `Toolset` subclasses and reads its
generic `McpServerOptions.Operations` enablement map; Operations creates no parallel registry or endpoint. Jobs and
Cache remain the physical execution owners.

`McpOperationalToolsetAttribute` associates each toolset with its configuration key and exact `@ops:{key}` grant
namespace. Disabled toolsets are filtered from listing and rejected by direct name invocation before tool execution.

## Runtime authority

`OpsGate.RequireGrant` derives the subject from `sub` or `NameIdentifier`, queries active exact `AgentGrant` rows, and
fails loudly with the missing resource. A wildcard Entity grant is intentionally insufficient. Destructive methods
return `OpsGate.DryRun` until `confirm` is true. Successful mutations append `AgentAction` with subject, resource,
action, target identity, and timestamp.

`JobsToolset` delegates to `IJobCoordinator`. `CacheToolset` delegates to `ICacheClient` and derives flush-all tags
from `ICachePolicyRegistry`; it does not inspect provider internals.

## Failure and operational boundaries

Grant and audit data use Koan Entity persistence and therefore inherit the selected data provider's durability and
isolation. Mutation plus audit are not one distributed transaction. Jobs/Cache provider failures remain visible as
tool failures. The package supplies no remote identity provider, grant issuer, approval workflow, retry, compensation,
or physical-backend administration.
