# Approval policy ownership

`ApprovalPolicyModule<TEntity>` is the consumer's binding of shared business policy to its Entity.
Its concrete consumer module is discovered by Koan; `Register` composes host-owned lifecycle
handlers, corrective MVC responses, the policy-information controller, and a typed policy value.
An immutable policy instance belongs to that module/host. No service locator, custom assembly
scan, or provider registration is needed in consumer code.

`ApprovalPolicy` evaluates stable prior state at `BeforeUpsert`, below HTTP and MCP projections.
Creating an approved row directly is rejected. An existing pending request may be approved only
within the package's limit. Approved subject, amount, and state cannot change, but consumer-owned
post-approval fields may change under their own additional lifecycle checks. `BeforeRemove`
retains approved records. MVC translates lifecycle cancellation into HTTP 409 with a reason code;
MCP supplies its own tool-error envelope for the same persistence rejection.

The host-owned policy is not a database constraint. The example demonstrates sequential operations
through Koan against local SQLite; it does not claim optimistic concurrency, multi-process serial
decisions, or protection against direct external database writes. Bypasses through advertised
application operations must be covered by the verification fixture before changing this policy.

The spending limit is intentionally an organization-owned package default. Each application
configures its own SQLite file; neither overrides the limit. Adopting a previous package can restore
earlier policy behavior, but it does not reverse stored business decisions or migrate data.

The workspace uses published Koan capabilities and a locally packed Core manifest repair,
with local Directory.Build and Directory.Packages
boundaries to reproduce downstream builds without repository-only generators or version pinning.
The sample is non-packable by default. The verifier enables packing for this foundation only,
uses an isolated Git history for NBGV, and writes exclusively to a local feed. It publishes nothing.
Run `prepare-framework.ps1` at the workspace root before building; `.local/framework.json` records
the computed Core version and package hash. This prerequisite remains until the fix is released.
