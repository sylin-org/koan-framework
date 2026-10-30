# Sylin.Koan.Identity.Tenancy — technical contract

## Ownership

This package is the only functional bridge between the otherwise independent Identity and Tenancy pillars. It owns
durable membership authorization for inbound tenant selection, request-scoped role projection, membership facts in
Identity's effective-access explanation, and explicit identity/seat lifecycle closure. It owns no authentication
protocol, bearer-token issuer, tenant CRUD API, or invitation ceremony.

`IdentityTenancyModule` binds and validates carrier options, registers four `ITenantResolver` implementations, adds one
ordered `IWebContextContributor`, and registers `DeprovisioningService`. Identity's existing discovered contributor
registries find `MembershipAccessContributor` and `IdentityTenancyErasureContributor`; the bridge does not create
another registry or activation ordering mechanism.

## Request isolation chokepoint

`TenantResolutionContributor` executes automatically in Web's ordered context lifecycle after authentication:

1. reject anonymous subjects before carrier/control-plane lookup;
2. read the resolvers by `TenantCarrierRank` for the tenant candidate: the lowest rank that names one decides, and two
   carriers of that rank naming different tenants reject the request with 404;
3. query one matching `Membership`, which both authorizes the candidate and supplies tenant roles;
4. load the durable `Identity` and require `IsActive`;
5. strip reserved Identity/Tenancy host roles, project the remaining standard role claims, and contribute
   `Tenant.Use(candidate)` for the rest of the request;
6. restore the original principal and ambient context on exit.

Unresolved or unauthorized requests continue unscoped rather than returning a tenant-existence oracle. Downstream
tenant-managed Data, Storage, and Cache operations enforce the missing-axis failure. Application code must consume
`Tenant.Current`; a carrier is an untrusted candidate, not an authorized context.

The durable Identity read intentionally occurs only after a matching membership and is not memoized. Suspension and
deactivation must affect the next request, including a request carrying a still-valid bearer principal.

## Carrier behavior

`TenancyResolutionOptions` binds from `Koan:Tenancy:Resolution`. Claim and header values may be tenant IDs or an
exact unique `TenantRecord.Code`; subdomain and path carriers resolve codes. Subdomain extraction accepts exactly one
label before a configured base host. Empty `BaseHosts` leaves only that carrier inert. Empty names/prefixes and empty
base-host entries fail startup.

Every resolver declares its `TenantCarrierRank`, so registration order decides nothing: `Address` (subdomain, path), then
`Request` (header), then `Session` (claim). The address wins rather than a disagreement being refused because the
ordinary disagreement is legitimate: a member of two tenants, whose session names the first, opens a page of the
second. Refusing would break moving between one's own tenants, and the page says what the person means. Only two
addresses that contradict each other leave nothing to go by, so only they are refused. Precedence is not a policy
override: every candidate passes the same active-membership check, and a candidate that fails it scopes nothing rather
than falling through to a lower rank. There is deliberately no `RequireMembership` option.

## Effective access and role authority

Inside an ambient tenant, `MembershipAccessContributor` adds each membership role to Identity's ordinary
`EffectiveAccessResolver`. Outside tenant scope it contributes nothing. The same membership row therefore explains
and enforces access without a second tenant-role model.

Role projection removes `TenancyRoles` and `IdentityRoles` host authority at one chokepoint even if a malformed import
or direct write placed such a value on a membership. Ordinary tenant roles use standard `ClaimTypes.Role` behavior.

## Deprovisioning semantics

Full deactivation writes `Identity.Status` first, then revokes Koan cookie sessions, removes the person's client grants
and removes all memberships. A client grant is tenant-scoped, so it removes those the ambient tenant reaches. The
status check keeps tenant entry fail-closed if a later cleanup write throws. Seat removal deletes matching membership
rows for only one tenant and does not revoke sessions or affect other seats.

These are multi-write workflows over the selected Data provider. They are not transactional across rows/providers and
can partially complete before an exception. No receipt is emitted on an incomplete workflow. A completed receipt is
host-scoped and stores counts, explicit surface names, time, and a SHA-256 content hash. `HasValidHash()` detects field
changes; it is not a signature, append-only guarantee, or proof of current external state.

For whole-person erasure, the bridge's discovered owner enumerates registered tenants, removes matching tenant-scoped
`AgentGrant` rows and host-scoped memberships, blanks identity IDs from prior deprovisioning receipts and recomputes
their hashes, and replaces identifying tenancy-audit actor/summary fields. Repeated execution is idempotent. Coverage
is limited to registered tenants and this package's owned records; it does not imply external tenant-system cleanup.

## Unsupported and deferred

- distributed single-claim invitation acceptance and its recovery state machine;
- public/anonymous tenant routing;
- verified custom-domain ownership and per-tenant carrier policy;
- tenant lifecycle-status enforcement;
- OAuth/bearer revocation or global authorization closure;
- cross-provider transactions and externally attested erasure certificates;
- a second generic contributor/election engine inside this bridge.
