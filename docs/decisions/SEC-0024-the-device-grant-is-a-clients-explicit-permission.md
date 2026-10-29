---
id: SEC-0024
slug: the-device-grant-is-a-clients-explicit-permission
domain: Security
status: Accepted
date: 2026-10-29
title: The device grant is a client's explicit permission
related:
  - SEC-0006
---

# SEC-0024: The device grant is a client's explicit permission

## Context

`/oauth/device` accepted any active client. Dynamic registration (SEC-0006 D5) answers a self-registered loopback client
with `grant_types: ["authorization_code"]`, yet nothing stored or checked that answer, so the same client could start
the Device Authorization Grant. A device request has no redirect to constrain it: the person reads a code on one screen
and types it into the application's consent page, which then shows the client's self-chosen name in front of an
approval. The loopback redirect that makes a dynamic client harmless in the code flow does not exist in the device flow.

Measured by a consumer's security review of its consent page.

## Decision

**A client is allowed the grants it names, and the device grant only by naming it.**

1. `OAuthClient.GrantTypes` lists the grants a client may use, as RFC 7591 `grant_types` names them; `OAuthGrantTypes`
   holds the three values the server implements (`AuthorizationCode`, `DeviceCode`, `RefreshToken`). An empty list
   means the authorization code grant only, RFC 7591's default, so every client registered before this change keeps the
   code flow. `OAuthClient.AllowsGrant(grantType)` answers the rule.
2. `/oauth/device` refuses a client not allowed `DeviceCode` with `400 unauthorized_client` (RFC 6749 §5.2) before it
   creates anything. `/oauth/authorize` refuses a client not allowed `AuthorizationCode` with `unauthorized_client` at
   its registered redirect, since the client and redirect are validated first.
3. Dynamic registration stores `GrantTypes = [authorization_code]` whatever the request asks, and answers with what it
   stored. A pre-registered client is allowed the device grant explicitly:
   `new OAuthClient { Id = "cli", ClientName = "CLI", GrantTypes = [OAuthGrantTypes.DeviceCode] }`.
4. The Development explorer client (`koan-dev-explorer`, SEC-0006 addendum) is seeded with the device grant, and a row
   seeded before this change is given it on the next Development start.

## Compatibility

- A pre-registered client that used the device flow must now name `OAuthGrantTypes.DeviceCode` in `GrantTypes`; until it
  does, `/oauth/device` answers `unauthorized_client`. A client that names grants and leaves out `AuthorizationCode`
  loses the code flow.
- Dynamic clients lose the device flow they were never meant to have; their code flow is unchanged.
- `OAuthClient.GrantTypes`, `OAuthClient.AllowsGrant` and `OAuthGrantTypes` are added. The token endpoint and the
  discovery document read the grant names from `OAuthGrantTypes`; their wire values are unchanged.

## Evidence

- `DeviceGrantSpec` (Koan.Web.Auth.Server.IntegrationTests): a client registered through `/oauth/register` asking for the
  device grant is answered `grant_types: ["authorization_code"]` and refused at `/oauth/device` with
  `unauthorized_client`; a pre-registered client that names no grant is refused there; one that names `DeviceCode` is
  given a `device_code`; one that names only `DeviceCode` is refused at `/oauth/authorize` with `unauthorized_client` at
  its redirect.
- `OAuthDeviceFlowSpec`, `OAuthHardeningSpec` and `DevExplorerClientSeedSpec` run the device flow with clients that name
  the grant, and the seeded explorer client.
