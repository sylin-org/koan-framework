# Sylin.Koan.Tenancy.Web technical contract

## Activation

`KoanTenancyWebModule` has no custom ordering attribute. Project references express availability and its registrations
are order-independent. It mounts `TenancyOperatorController`, contributes one `BeforeRouting` exposure middleware,
registers `TenantAdministrationService`, and adds one named authorization policy. It does not change the application's
default or fallback authorization policy.

The package depends on Web and Tenancy plus `Microsoft.AspNetCore.App`. It does not depend on Jobs or Identity.

## HTTP ownership

All routes are attribute-routed controllers. `TenancyConsoleUiController` serves embedded assets under `/tenancy`;
`TenancyOperatorController` owns `/api/tenancy/admin` and translates HTTP only. Mutation semantics live in
`TenantAdministrationService`.

Supported API surface:

- `GET /tenants`
- `GET /tenants/{id}`
- `POST /tenants`
- `POST /tenants/{id}/rename`
- `POST /tenants/{id}/memberships`
- `DELETE /memberships/{membershipId}`
- `GET /audit?tenantId=&size=`

Roster and detail intentionally return the complete registry/membership set requested by those endpoints. Audit is an
explicit bounded projection: the caller may request a size up to the configured maximum; the default comes from
`TenancyConsoleOptions` rather than an adapter-selected page.

## Mutation chokepoint

`TenantAdministrationService` normalizes names/codes, rejects a duplicate routing code, gives a membership its
deterministic key, defaults an empty role set to `TenancyRoles.Member`, rejects the host operator role, and writes the
audit entry. Repeating an equivalent membership grant preserves one seat and does not add audit noise.

These writes are ordered and idempotent where stated; they are not transactional. Tenant-code resolution in Identity
Tenancy still denies ambiguity, closing over direct Entity writes that bypass the supported administration path.

## Exposure, authority, and options

`TenancyConsoleExposureMiddleware` recognizes the fixed UI/API prefixes. `Enabled`, host allow-list, and required
header are forgeable routing signals; a miss returns 404. `OperatorAuthorizationHandler` independently admits Open-
posture development or a configured identity/host role in Closed posture. Membership roles cannot confer the host
operator role because Identity Tenancy strips reserved host roles again at request projection.

`TenancyConsoleOptions` binds from `Koan:Tenancy:Console` and validates positive, coherent audit page sizes at startup.
The module report reads the same configuration family and states effective activation.

## Unsupported surfaces

No current type or route claims invitation, suspension, tenant data erasure, resumable lifecycle operations, or
server-side act-as. Those concerns need their own pillar/chokepoint contracts because registry fields or audit-only UI
state cannot create the application guarantees their names imply.
