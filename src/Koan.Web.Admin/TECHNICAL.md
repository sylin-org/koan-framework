---
uid: reference.modules.koan.web.admin
title: Koan.Web.Admin - Technical Reference
description: Development-only Admin routing, authorization, provenance projection, and sanitization contract.
since: 0.17.0
packages: [Sylin.Koan.Web.Admin]
source: src/Koan.Web.Admin/
---

## Contract

`Koan.Web.Admin` is a Development-only HTTP projection, not a second runtime registry or control plane. Package
discovery registers its controllers, one MVC route convention, one authorization filter, and options validation
through the application's existing `AddKoan()` call.

The UI reads only the status API. Both API actions project current canonical framework sources directly:

| Surface | Authority |
|---|---|
| modules, settings, notes, tools | `KoanEnv.Provenance`, falling back to `ProvenanceRegistry` |
| pillar label, color, icon | `KoanPillarCatalog` |
| environment | `KoanEnv.CurrentSnapshot` |
| component health | `IHealthAggregator` when registered |
| process, memory, GC, thread pool | bounded .NET runtime APIs |

There is no Admin manifest registry, feature manager, route service, style parser, topology generator, or public
service interface. Public Admin records are the JSON wire contract; internal classes own capture and delivery.

## Activation and authorization

Every Admin controller uses one authorization filter. The filter first requires both
`IHostEnvironment.IsDevelopment()` and `KoanAdminOptions.Enabled`; otherwise it returns 404. It then delegates to
`IAuthorizationService` using `Koan:Admin:Authorization:Policy`.

When `AutoCreateDevelopmentPolicy` is true and the application has not already registered the named policy, Admin
adds a Development policy with `RequireAuthenticatedUser()`. An application policy with the same name wins. Failed
anonymous authorization challenges through the application's configured authentication scheme; an authenticated
user that fails the policy is forbidden.

Admin intentionally supplies neither an identity nor an authentication handler. Authentication remains an
application choice expressed with standard ASP.NET Core services.

## Startup-owned routes

MVC application-model construction snapshots `KoanAdminOptions.PathPrefix` and replaces the package's internal route
placeholders. The default resolved map is:

| Purpose | Route |
|---|---|
| UI and embedded assets | `/.koan/admin/{**asset}` |
| status | `/.koan/admin/status` |
| health | `/.koan/admin/health` |

The prefix accepts letters, digits, `.`, `-`, and `_`, with optional surrounding slashes removed. Route changes need
a host restart because ASP.NET Core endpoint construction is startup-owned; the package does not advertise dynamic
route reload.

## Redaction boundary

Every provenance setting marked secret receives the constant wire value `********`; its original value is never
copied into an Admin DTO. Runtime capture is permanently sanitized and locked. The following fields are null (and may
be omitted by the application's JSON policy):

- process user name, command line, executable path, and working directory;
- machine name and domain name.

The response lists the sanitized field names and a lock reason so the omission is explainable. There is no option to
weaken this boundary. Remaining process facts are bounded diagnostics such as PID/name, uptime, CPU, memory, GC, and
thread-pool counters.

## Degraded health

If no `IHealthAggregator` is registered, or a snapshot cannot be obtained, the health document reports `Unknown`
with no components. A health projection failure does not remove the provenance/status evidence that may explain it.

## Embedded UI

The embedded HTML, CSS, and JavaScript are served only through the authorized Admin controller with `no-store` cache
control. Asset resolution uses exact embedded-resource names, rejects traversal segments, and does not enumerate the
assembly resource table per request. Rendering uses DOM `textContent`; the UI makes no service-mesh, topology,
mutation, logging, or raw-manifest request.

## Focused evidence

`Koan.Web.Admin.Tests` boots real ASP.NET Core `TestServer` hosts through package discovery and `AddKoan()`. It proves
automatic mounting, the Development/disabled 404 boundary, standard challenge/forbid/success policy results, route
relocation, startup validation, permanent secret/identity sanitization, and absence of the retired control-plane
routes.
