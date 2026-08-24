---
type: REFERENCE
domain: security
title: "Sign-in"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: passed
  scope: docs/capabilities/trust/sign-in.md - cold-executed via let-people-sign-in on the Test
    connector path: eligibility evidence, code flow over HTTP, anonymous 401 behind [Access],
    logout revocation, fail-closed startup on an incomplete provider intent
---

# Sign-in

Establish a real caller identity through an existing account provider, an application-owned token
issuer, or tokens minted elsewhere.

## You need

| Piece | Package | Note |
|---|---|---|
| Authentication runtime | `Sylin.Koan.Web.Auth` | configuration-only OIDC/OAuth2 providers need no connector package |
| Google | `Sylin.Koan.Web.Auth.Connector.Google` | connector brings the runtime transitively |
| Microsoft | `Sylin.Koan.Web.Auth.Connector.Microsoft` | connector brings the runtime transitively |
| Discord | `Sylin.Koan.Web.Auth.Connector.Discord` | connector brings the runtime transitively |
| Deterministic development and tests | `Sylin.Koan.Web.Auth.Connector.Test` | never a production identity path |

## The constraint box

> **The constraint:** Authentication only establishes who is acting. It grants no Entity permission
> by itself. A referenced connector without complete credentials is inert; credentials stay out of
> source, and the Test connector must never be allowed to present itself as production success.

## Choose who owns identity

| Shape | Add | What changes |
|---|---|---|
| Users already belong to Google, Microsoft, Discord, or another compliant provider | the matching connector, or configuration-only OIDC/OAuth2 | this application consumes identity |
| This application must mint tokens | `Sylin.Koan.Web.Auth.Server` | this application becomes an identity provider |
| Tokens are minted elsewhere and only validated here | `Sylin.Koan.Security.Trust` | inbound trust, not interactive sign-in |
| A person must survive provider changes | `Sylin.Koan.Identity` · `Sylin.Koan.Identity.Web` | durable person identity is separate from provider subject |

## Leaves

- **Build and provider proof:** [let people sign in](../../recipes/let-people-sign-in.md)
- **Setup contract:** [authentication guide](../../guides/authentication-setup.md)
- **Trust contract:** [auth guide](../../guides/auth-howto.md)

After identity exists, continue to [access rules](access-rules.md); sign-in alone protects nothing.
