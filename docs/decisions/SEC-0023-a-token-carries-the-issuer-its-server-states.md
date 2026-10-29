---
id: SEC-0023
slug: a-token-carries-the-issuer-its-server-states
domain: Security
status: Accepted
date: 2026-10-29
title: A token carries the issuer its authorization server states
related:
  - SEC-0001
  - SEC-0006
  - WEB-0078
---

# SEC-0023: A token carries the issuer its authorization server states

## Context

Trust stamped every token with `Koan:Security:Trust:Issuer`, `koan-dev` by default, and `Koan.bearer` accepted that name
only. The embedded authorization server's discovery metadata states its issuer as `Koan:Web:Auth:Server:Issuer` or the
application's base URL (WEB-0078). An access token the server issued therefore carried `iss: "koan-dev"` while the
metadata said `https://example.test/notes`, and a verifier outside the application that reads the metadata and the
JWKS — as RFC 8414 and RFC 9068 describe — refused every token. The base URL may also be derived from each request in
Development, so the issuer is not always one fixed string at configuration time.

Measured by a consumer whose base URL carries a path.

## Decision

**An application has one issuer per request, the one its authorization server states, and every token carries it.**

1. Trust gains `IIssuerIdentifier`, whose `Resolve()` states the issuer identifier for the current request, or null
   when none can be stated. `EcdsaIssuer` stamps the stated issuer, else the named `TrustIssuerOptions.Issuer`, else
   `koan-dev`. `TrustIssuerOptions.Issuer` is now optional: null means the application names none, and a blank value is
   refused at start.
2. `Koan.bearer` accepts the issuer stated for the request being validated and the named-or-default name, through an
   issuer validator that reads the identifier at validation time; any other issuer is refused even when the signature
   is this application's. The same rule applies to `IIssuer.TryValidate`.
3. The authorization server registers `AuthServerIssuer` as the identifier. Its issuer is
   `Koan:Web:Auth:Server:Issuer` when set, else a named `Koan:Security:Trust:Issuer`, else the base URL — configured,
   or derived from the request where WEB-0078 allows it. The discovery metadata states the same value, so `iss` equals
   the metadata's `issuer` at the root and under a path. Outside a request, only a configured value can be stated, so a
   token minted there in the request-derived posture keeps the named-or-default name, which the bearer accepts. Where
   Production refuses to derive the base URL, the metadata refuses with WEB-0078's correction and tokens keep Trust's
   own name, as before.
4. The advertised endpoints still derive from `Koan:Web:Auth:Server:Issuer` or the base URL; only the `issuer` member
   can differ, and only when the application names `Koan:Security:Trust:Issuer` without naming the server's own.

An application that names an issuer keeps it: `Koan:Web:Auth:Server:Issuer` is the issuer of the metadata and of every
token, and a named `Koan:Security:Trust:Issuer` is too when the server's own is unset.

## Compatibility

- Tokens issued before this change carry `koan-dev` (or the named Trust issuer). By decision, Koan's own bearer scheme
  keeps accepting that name under this application's own signature — the same key ring, so it confers nothing a forger
  could use — and so keeps accepting those tokens until they expire — fifteen minutes for an access token by default, at most a day for a dev token —
  because the named-or-default name stays accepted. A verifier outside the application refuses them, as it did before.
  A client holding a refresh token receives a token with the stated issuer at its next refresh; the refresh token is
  opaque and server-side, so it is unaffected.
- New tokens from an application that references the authorization server carry its base URL (or its named issuer)
  instead of `koan-dev`. A consumer that compared `iss` with `koan-dev` must compare it with the metadata's issuer.
- An application that names `Koan:Security:Trust:Issuer` and references the authorization server now sees that value
  as the metadata's `issuer` when `Koan:Web:Auth:Server:Issuer` is unset; it should be the application's public URL for
  an RFC 8414 client to accept the metadata.
- `TrustIssuerOptions.Issuer` becomes `string?` (null when unnamed); `IIssuerIdentifier` and an optional identifier
  parameter on `EcdsaIssuer`'s constructor are added. `IIssuer` is unchanged.

## Evidence

- `TokenIssuerSpec` (Koan.Web.Auth.Server.IntegrationTests, Kestrel at the root in Development, no base URL): a code-flow
  access token's `iss` equals the metadata's `issuer`, the request-derived origin, and an endpoint that only
  `Koan.bearer` admits accepts it.
- `TokenIssuerUnderPathSpec` (base URL `https://www.example.com/tangent`): a device-flow access token's `iss` equals the
  metadata's `issuer`, the base URL with its path, and `Koan.bearer` accepts it; a token signed by the same key under
  `https://elsewhere.example` is refused with 401 while the same key under the base URL is accepted.
- `NamedIssuerSpec`: with `Koan:Security:Trust:Issuer=https://trust.example`, or `Koan:Web:Auth:Server:Issuer=https://server.example/`,
  the metadata's `issuer` and a minted token's `iss` are that value (without the trailing slash) and the token
  verifies.
- `IssuerIdentifierTests` (Koan.Security.Trust.Tests): without an identifier the named issuer or `koan-dev` is
  stamped; a stated issuer is stamped and accepted, and a token minted while nothing was stated carries `koan-dev` and
  is still accepted; a token under another issuer is refused with this issuer's key; a blank named issuer fails options
  validation.
