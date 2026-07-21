# SEC-0003: Dev & Shared-Secret Identity — default-anonymous, a default shared secret for self-mint, fail-closed in production

**Status**: **Accepted (2026-06-07)** — architect-directed correction.
**Date**: 2026-06-07
**Deciders**: Enterprise Architect
**Scope**: Corrects the SEC-0001 §4 Rung-0/1 *implementation*. The zero-config dev experience is **default-anonymous** with an explicit **test login** (the TestProvider) and an explicit `?_as=` override; the trust issuer signs HS256 with a **default shared secret** so every service self-mints with zero config; a **fail-closed guard** keeps that default out of non-dev environments.
**Related**: **SEC-0001 §4** (amended — Rung 0/1) · SEC-0001 §11 (footgun removal — contrast in §2.4) · WEB-0043/0045 (TestProvider) · corrects increments 2c / 2e / 2g / 2h.

---

## 1. Context — what the first implementation got wrong

SEC-0001 Phase 2 implemented the zero-config dev experience as an **ambient auto-sign-in** as `dev` with role `admin` (full access). For the real workloads this targets — applications whose UX varies by profile, and which are evaluated **~99% of the time as the public / anonymous interface** — that is actively wrong:

- it forces the **admin** view when you almost always want the **public** view;
- it **hides the real token path** (every request is already an admin, so the login flow and the unauthenticated/forbidden paths are never exercised);
- changing "who you are" required remembering `?_as=anonymous`, i.e. opting *out* of being admin.

The model intended from the original design discussion was the inverse: a **test login page** where you pick a profile and receive a **real signed token**, with **anonymous as the default**. This ADR restores that and pins the signing-key story.

## 2. Decision

### 2.1 Default = anonymous
No ambient authentication. A fresh request is unauthenticated and the app renders its public UX. `[Authorize]` is enforced even in Development.

### 2.2 The dev login is the TestProvider — zero-config in Development
Clicking **Login** routes to the built-in **TestProvider** login page, where you set **subject + roles + permissions + custom claims**, and are redirected back with a real signed token + cookie session — the full OAuth `code → token → cookie` flow, so the actual auth path is exercised (no masking). The TestProvider is available **zero-config in Development** (no `Enabled: true` required) and remains **opt-in elsewhere** (structurally absent in production unless explicitly enabled). *Corrects 2h's blanket demotion.*

### 2.3 `?_as=` quick override — kept, explicit, default-off
For scripted / automated testing, `?_as=<sub>&_roles=a,b&perms=…` sets a **transient per-request** synthetic principal (no cookie); omitting `?_as` — or `?_as=anonymous` — is anonymous. This is the explicit counterpart to §2.1: it never authenticates **by default**. *Corrects 2e.*

### 2.4 A default shared secret — every service self-mints
The issuer signs **HS256** with `Koan:Security:Trust:Key`, which **defaults to the well-known value**:

```
super-insecure-shared-secret-replace-asap
```

Because the default is identical everywhere, **every Koan service self-mints valid tokens with zero configuration**, and services sharing the key (same box, or different boxes with the same value) trust each other's tokens out of the box. The 256-bit HMAC key is `SHA-256(secret)`, so any secret length is valid and all holders derive the same key.

This is the deliberate **opposite** of the deterministic-HMAC footgun SEC-0001 §11 removed:

| | Old footgun (removed) | This default key |
|---|---|---|
| Derivation | from **public** info (`issuer:audience:…`) | a literal, **named** constant |
| Perception | looked secret → **false** sense of security | **loudly insecure by name** |
| Reaches prod? | silently, yes | **no** — fail-closed (§2.5) |

A loudly-insecure, replace-me default is safe *because* it is honest and structurally barred from production; a sneaky-but-forgeable "secret" is not.

### 2.5 Fail-closed in real deployments — and a very loud warning everywhere else
The boot guard **refuses to start** in a **real-deployment environment (Production or Staging)** when the effective key is still the default insecure value — unless explicitly acknowledged with `Koan:Security:Trust:AllowInsecureKeyInProduction=true` (for a throwaway box). The fix is to set `Koan:Security:Trust:Key` to a real secret. **Development and test environments (e.g. `Test`, `Testing`) boot** so local work and integration hosts are zero-config.

Wherever the default key is active *and the app still boots* (Development, Test, or an acknowledged real box), bootstrap emits a **very loud warning banner on every start** — a multi-line, framed log message naming the key and telling you to replace it — so the reminder is impossible to miss and the default never quietly lingers into a real environment. The boot report always states the active posture as well.

### 2.6 Crypto: symmetric now, asymmetric fleet later
Rung 0/1 is **symmetric** (HS256, shared secret) — the accepted "whoever holds the key can mint" trade-off, which is fine for a single developer or a small trusted team. **Per-node asymmetric identity + enrollment (no shared secret)** remains the SEC-0001 fleet roadmap (Rung 2); it elects in when a real issuer is configured (`Koan:Security:Trust:Issuer`).

## 3. Token contract

- **Mint** (any service, zero-config): inject `IIssuer`, call `Issue(TrustClaims)` — signed with the shared key.
- **Validate inbound**: the `Koan.bearer` scheme validates HS256 against the same key.
- The **TestProvider login** and the **backend self-mint** both yield tokens valid under that one key.

## 4. Posture (boot report)

| Mode | Trigger | Use |
|---|---|---|
| `DefaultInsecure` | key unset or equals the well-known default | **dev only** — fail-closed elsewhere |
| `SharedKey` | `Koan:Security:Trust:Key` set to a custom secret | solo / small trusted team / staging |
| `Configured` | `Koan:Security:Trust:Issuer` set | fleet / federated (future, Rung 2) |

## 5. Consequences

- **Public-first apps work as expected in dev** (anonymous by default); login is an explicit, profile-choosing act that exercises the real path.
- **Zero-config self-mint across services** via the default key — no per-service token wiring.
- The insecure default is **honest and structurally barred from production**.
- The symmetric trade-off is accepted for dev / small-team; the fleet asymmetric tier is the upgrade, not a rewrite (same `Issue` / `Koan.bearer` / `Identity.Current` surface).

## 6. Alternatives considered

1. **Ephemeral per-process key** (the prior 2c implementation). Self-mint can't cross services or restarts. Rejected — defeats "every service self-mints."
2. **No default key (require explicit config).** Breaks zero-config self-mint; every new app hits a wall. Rejected.
3. **Asymmetric (ES256) now.** Per-node enrollment is the fleet tier; overkill for a single developer and can't be self-minted zero-config without distributing keys. Deferred to Rung 2.

## 7. Migration (corrects SEC-0001 Phase 2)

| Increment | Was | Now |
|---|---|---|
| 2c | `DevIssuer` — ES256, per-process ephemeral | `SharedKeyIssuer` — HS256, default shared secret |
| 2e | automatic development identity originally lived in middleware | default-anonymous; `DevIdentityContributor` acts only on explicit `?_as=` through Web's ordered context lifecycle |
| 2g | fail-closed on `DevEphemeral` in Production | fail-closed on the **default-insecure key** outside Development |
| 2h | TestProvider demoted to opt-in everywhere | zero-config in Development; opt-in elsewhere |

## 8. References

- SEC-0001 §4 (Rung 0/1, amended) · §11 (footgun removal) · `Koan.Security.Trust.Issuer` · `Koan.Web.Auth.Connector.Test` (TestProvider) · `docs/guides/auth-howto.md`.
