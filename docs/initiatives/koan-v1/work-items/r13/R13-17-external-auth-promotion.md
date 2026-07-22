---
type: SPEC
domain: web
title: "R13-17 - Promote external authentication connectors"
audience: [architects, maintainers, developers, ai-agents]
status: resolved
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: passed
  scope: Google, Microsoft, and Discord connector definitions, shared authorization-code runtime, packages, consumer, product, and API evidence
---

# R13-17 — Promote external authentication connectors

## Architecture checkpoint

**Task:** Promote Google, Microsoft, and Discord as three installable provider integrations over the
already-supported Web Auth runtime without contacting live identity services or duplicating its flow engine.

**Application intent:** An application references one connector, supplies provider-issued credentials,
keeps ordinary `AddKoan()`, and begins sign-in at `/auth/{provider}/challenge`.

**Public expression:**

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

```json
{
  "Koan": { "Web": { "Auth": { "Providers": { "google": {
    "ClientId": "{GOOGLE_CLIENT_ID}",
    "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
  } } } } }
}
```

**Guarantee/correction:** Each connector contributes its exact authority/endpoints, scopes, display
metadata, and priority. Reference alone is inert. Complete explicit credentials activate the provider;
incomplete intent fails with the exact missing fields. Web Auth owns PKCE, correlation/nonce, callbacks,
claims, cookies, identity linking, plan election, and reporting.

**Complete intent surface:** Reference a connector, configure credentials and any provider-console
redirect/tenant policy, call `AddKoan()`, and use the stable challenge route. No connector-specific
registration, handler, middleware, controller, or client is required.

**Docs/code read:** Governing engineering/architecture rules, Auth guides, all three package companions,
the three connector modules, `AuthProviderDefinition`, `AuthProviderPlan`, and the deterministic OAuth2/OIDC
integration round trips were read. The connectors remain definition-only adapters; Web Auth remains the
single protocol/runtime owner.

**Reusing:** Existing provider-definition grammar, compiled plan, maintained ASP.NET handlers, deterministic
Test connector protocol service, Auth unit/integration suites, package compiler, API guard, lean PR gate,
and main publisher.

**Creating new:** One focused spec that consumes the real three modules and proves inertness, exact protocol
defaults, eligibility, challenge routes, and deterministic election; one product claim; this evidence card.
No live credentials, mock provider SDK, provider-specific handler, certification layer, or new workflow.

**Baseline correction:** The six R13-16 public API floors are recorded by the existing repository build-policy
owner rather than by editing their package projects. This preserves SDK validation without minting six
behaviorless patch packages. Existing project-local floors remain valid; later promotions use the central owner.

## Evidence boundary

1. Run the focused external-definition tests and the existing deterministic OAuth2/OIDC authorization-code
   integration suite; do not contact Google, Microsoft, or Discord.
2. Pack the three exact owners with `PublicRelease=true` and inspect supported dependency bands.
3. Run one clean staged-package consumer through normal `AddKoan()` activation and compiled provider discovery.
4. Compile product truth, run API posture and lean no-tests coherence, publish through `main`, then rerun the
   same consumer from NuGet.org only.
5. Do not run Identity Server, unrelated Auth/Identity owners, live credentials, or whole-framework certification.

## Current evidence

- `Koan.Web.Auth.Tests`: 41/41 passed, including direct consumption of all three connector modules.
- `Koan.Web.Auth.Integration.Tests`: 5/5 deterministic OAuth2/OIDC authorization-code round trips passed.
- Exact `PublicRelease=true` packs produced Google, Microsoft, and Discord `0.20.0` packages and symbols;
  each depends on supported `0.20.*` Core, Web Auth Abstractions, and Web Auth bands.
- A fresh-cache staged package-only application used ordinary `AddKoan()`, elected Google by priority,
  discovered all three eligible providers, and emitted
  `EXTERNAL-AUTH|PACKAGE-CONSUMER|ADDKOAN|GOOGLE|MICROSOFT|DISCORD|PASS`.
- Generated product truth is 43 claims across 93 packages. API posture is 73/76 assembly packages with
  immutable floors; only these three legitimate first publications are pending. The six R13-16 floors
  are active centrally without changing those package versions.
- Lean no-tests coherence passed all eight legs in 66.3 seconds.
- PR `#107` passed lean gate `29926425619` and squash-merged as
  `a12b2154907d9f75f8bdef77cf4470ecefa1aad8`.
- Release run `29926734114` accepted exactly the three `0.20.0` connector packages and symbols. The
  already-public Storage/Media identities remained at `0.20.0` and were skipped as duplicates; no
  behaviorless patch packages were created.
- NuGet.org indexed all three exact versions. The unchanged application restored from NuGet.org only
  into an empty cache, built with 0 warnings/errors, loaded all three modules, elected Google, and emitted
  `EXTERNAL-AUTH|PACKAGE-CONSUMER|ADDKOAN|GOOGLE|MICROSOFT|DISCORD|PASS`.
- All three immutable API floors are now recorded centrally without editing their package-owned paths;
  the focused guard reports `76/76 configured, 0 first-publication pending, 3 content-only`.
