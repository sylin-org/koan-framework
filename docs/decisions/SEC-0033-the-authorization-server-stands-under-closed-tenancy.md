---
id: SEC-0033
slug: the-authorization-server-stands-under-closed-tenancy
domain: Security
status: Accepted
date: 2026-10-30
title: The authorization server's protocol is the host's, and a person's rows stay in their tenant
related:
  - SEC-0005
  - SEC-0006
  - ARCH-0095
  - ARCH-0100
---

# SEC-0033: The authorization server's protocol is the host's, and a person's rows stay in their tenant

## Context

Measured at `bd70b7b01`. `Koan.Web.Auth.Server` could not start under a Closed tenancy posture: its signing-key store read a
tenant-scoped entity with no tenant in scope and threw `SegmentationRequiredException` out of the module's `Start`. Its cleanup
service could not sweep there either (SEC-0005, decision 6), because Koan had no sanctioned scope for maintenance across tenants.

## Decision

1. **The protocol's rows are the host's.** `IssuerSigningKeyRecord`, `OAuthClient`, `AuthorizationCode`, `ConsentRequest`,
   `DeviceCode` and `RefreshToken` implement `IAmbientExempt`. There is one issuer per host, a client calls the token endpoint
   with no tenant to name, and every row is found by an opaque key. The marker moves no rows.
2. **A person's rows stay in their tenant.** `AgentGrant` and `RememberedConsent` stay tenant-scoped. A grant confers a capability,
   so a grant in one tenant must not confer it in another, and a remembered consent must not approve silently there.
3. **Maintenance has a sanctioned scope.** `IMaintenanceScopes.ForEach(work, ct)` runs work once per scope. The Data default is
   one scope, the ambient one. `Koan.Tenancy` replaces it with the tenant already in effect, if any, then one per other
   `TenantRecord`, read a page at a time. It is `ForEach` and not an enumerator because an ambient scope pushed inside an async iterator does
   not reach its consumer.
4. **The sweep uses it.** `OAuthArtifactCleanupService` removes the protocol's expired rows once, and expired remembered consents
   and client grants in each scope. A failure in one scope ends that sweep and is logged; the next tick runs it again.

5. **Closing a person reaches every tenant.** `ClientApprovals.Revoke` removes a person's client grants through
   `IMaintenanceScopes`, so suspending, deactivating or erasing someone ends their agents' approvals in every tenant, with any
   tenant in scope or none, and reactivating them brings no agent back.

## What stays, named

Under the Closed posture, whatever reads a grant needs a tenant in scope. A refresh at the token endpoint resolves one only where
the host's tenant carrier can (a subdomain, a path or a header); with a claim carrier it fails closed. That is the tenancy guard
doing its work, and the record does not change it.

## Consequences

- Public: `Koan.Data.Abstractions.IMaintenanceScopes`. The six entities above implement `IAmbientExempt`.
- SEC-0005's decision 6 is amended to name this record.
- Evidence: `ClosedTenancySpec` in `Koan.Web.Auth.Server.Tenancy.Tests` starts the server under Closed tenancy on a persisted key
  that a second start reads back, seeds the development client, and sweeps two tenants and the host while keeping live rows.
