---
type: RECIPE
recipe: let-people-sign-in
title: "Let people sign in"
domain: identity
status: current
last_updated: 2026-08-24
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/recipes/let-people-sign-in.md - cold-executed on the Test connector path: provider
    eligibility via /.well-known/auth/providers, full code flow over HTTP (persona cookie), anonymous
    401 behind [Access], logout revocation, and fail-closed startup on an incomplete provider
      intent
gets_you: "Real users authenticated through a provider they already have an account with."
works_if: "The application serves HTTP."
costs: "Adds a credential to hold and rotate per provider, and a callback URL per environment."
ingredients:
  - "one | authentication runtime | Sylin.Koan.Web.Auth"
  - "one-or-more | identity provider, user's choice | Sylin.Koan.Web.Auth.Connector.Google, Sylin.Koan.Web.Auth.Connector.Microsoft, Sylin.Koan.Web.Auth.Connector.Discord, Sylin.Koan.Web.Auth.Connector.Test"
  - "optional | issue tokens from this application instead | Sylin.Koan.Web.Auth.Server"
  - "optional | a durable person that survives provider changes | Sylin.Koan.Identity, Sylin.Koan.Identity.Web"
  - "optional | trust tokens minted elsewhere | Sylin.Koan.Security.Trust"
---

# Let people sign in

Reference a provider connector, supply its credentials, and `AddKoan()` compiles the provider plan and
maps the endpoints.

## Choosing the shape

The branch that matters is **who owns the identity**, and it is worth asking directly:

- **Someone else already does.** Their users have Google, Microsoft, or Discord accounts. Reference
  that connector — it brings the auth runtime transitively, so it is the only package to add.
- **A provider exists but has no connector.** Any compliant OIDC/OAuth2 provider works as
  **configuration only** — set its type, endpoints, and credentials. **No connector package is needed**,
  and reaching for one that does not exist is the common wrong turn here.
- **This application is the identity provider.** Other services need tokens from *you* — that is
  `Sylin.Koan.Web.Auth.Server`, a materially larger commitment than consuming a provider.
- **Tokens are minted elsewhere and only validated here.** That is inbound trust, not sign-in.

Then, separately: **should a person outlive their login?** If a user might change email, use two
providers, or the organization might switch tenants, a durable person identity is much cheaper to add
now than to retrofit once rows reference a provider-specific subject.

For local development and deterministic tests, the Test connector avoids standing up a real provider.
Never let it reach an environment that matters.

## Assembly

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Google
```

That is the whole install — the connector brings `Sylin.Koan.Web.Auth` with it. Configure the provider
under `Koan:Web:Auth:Providers` with its client credentials and callback.

`GET /.well-known/auth/providers` returns the providers actually eligible, and startup reporting plus
composition facts explain provider state, eligibility, election, and the correction when one is
misconfigured. Read those before guessing.

Depth: [auth how-to](../guides/auth-howto.md) · [authorization how-to](../guides/authorization-howto.md).

## Prove it

1. **Behavior** — an anonymous request is refused; a signed-in request succeeds; sign-out revokes.
2. **Composition** — assert the intended provider is the elected one, not a development fallback.
3. **Correction** — declare a provider intent with missing credentials and assert the host refuses
    to start, naming the config key and both remedies ("Set the missing values under
    `Koan:Web:Auth:Providers:{id}` or disable/remove that provider intent"), rather than the
    application appearing healthy with no way to log in.

## Boundaries

- Authentication is not authorization. Knowing who someone is says nothing about what they may do —
  protect the Entity or operation, not only the pipeline.
- Never invent a production identity or weaken policy to keep a demonstration working.
- Provider credentials are secrets. They belong in configuration the repository does not carry.

## Interacts with

**Tenancy.** Which tenant a signed-in person belongs to is a separate decision from who they are, and
an invitation binding the two is where most multi-tenant applications get this wrong.

**Agents and MCP.** An agent surface must reach the same authorization rules as HTTP, or it becomes a
way around sign-in entirely.
