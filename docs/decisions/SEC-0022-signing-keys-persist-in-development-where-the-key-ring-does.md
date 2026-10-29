---
id: SEC-0022
slug: signing-keys-persist-in-development-where-the-key-ring-does
domain: Security
status: Accepted
date: 2026-10-29
title: Signing keys persist in Development where the key ring does
related:
  - SEC-0006
  - WEB-0079
---

# SEC-0022: Signing keys persist in Development where the key ring does

## Context

SEC-0006 D1 persists the authorization server's ES256 signing keys outside Development and keeps an ephemeral
per-process key in Development. WEB-0079 then gave the application one setting, `Koan:Web:KeyRingDirectory`, that keeps
its Data Protection ring, and so its session cookies, across restarts in every environment. A local install that names
that folder kept its people signed in across a restart while every token its authorization server had issued stopped
verifying: the agent a person approved had to be approved again after each restart.

Measured by a consumer whose local install runs in Development with a persistent data folder.

## Decision

**The signing keys persist wherever the application has said its key ring persists.**

1. `AuthServerModule` chooses `PersistedIssuerKeyStore` outside Development, as before, and in Development when
   `KoanWebKeyRing.IsConfigured` — the application named `Koan:Web:KeyRingDirectory`. With no folder named, Development
   keeps Trust's ephemeral store. The persisted store protects each key with the Data Protection ring, which then lives
   in that folder, and keeps the records in the configured Data provider, as outside Development.
2. The rotation service rotates whichever persisted store is in force, and reads no environment: an ephemeral store
   has nothing to rotate.
3. The startup note states which store is in force, and in Development without a folder names the setting that keeps
   the keys.

The dev-token endpoint and the Development explorer client stay Development-only; the fail-closed guard outside
Development is unchanged.

## Compatibility

- A Development host that names `Koan:Web:KeyRingDirectory` now needs its Data provider at start for the signing keys,
  as a non-Development host always did; its tokens survive a restart when that provider is durable. A Development host
  with an in-memory provider persists keys that the next start does not find, and so behaves as before.
- A Development host that names no folder composes exactly as before.
- `IssuerKeyRotationService` depends on `IIssuerKeyStore` instead of `PersistedIssuerKeyStore` and the environment. No
  public surface changes.

## Evidence

- `DevelopmentSigningKeySpec` (Koan.Web.Auth.Server.IntegrationTests, two TestServer hosts started one after the other
  in Development over one JSON data folder): with a key ring folder named, the first host's store is
  `PersistedIssuerKeyStore`, the second host publishes the first host's key id and verifies the token the first host
  issued; with none, the store is `EphemeralIssuerKeyStore`, the second host publishes a different key and refuses
  the first host's token.
