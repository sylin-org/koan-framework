---
id: SEC-0034
slug: a-service-auth-token-proves-its-issuer
domain: Security
status: Accepted
date: 2026-10-30
title: An inbound atproto service-auth token proves its issuer, in the connector that resolves DIDs
related:
  - WEB-0082
  - DX-0043
---

# SEC-0034: An inbound atproto service-auth token proves its issuer, in the connector that resolves DIDs

## Context

An atproto service-auth token is how one atproto service, or an account's PDS on its behalf, authenticates to another:
a JWT its issuer's signing key signed, addressed to one service and bound to one method. Any application that takes
inbound atproto calls needs to verify one, and an agent with no browser needs a way to sign in. The connector already
owned the guarded DID resolution the check depends on and shipped nothing that used it for this, so a consumer had to
write ECDSA low-S verification, multikey decoding and JWT claim rules itself.

**Application intent:** "This token proves it came from this DID, for this service and this method, so sign them in."

**Expression:** a reference to `Sylin.Koan.Web.Auth.Connector.Atproto`, already the reference for atproto sign-in, and

```csharp
var proof = await serviceAuth.Verify(token, audience: "did:web:app.example#service", method: "org.example.signIn", ct);
if (proof.Verified) await serviceAuth.SignIn(HttpContext, proof);
```

## Decision

`AtprotoServiceAuth` lives in the connector, the one owner of DID resolution and of atproto's guarded transport, under
DX-0043's name for it. It verifies in this order, so a stranger's token reaches the network only once it could be genuine:

1. **Shape.** At most 8192 characters; three base64url parts; JSON objects with no duplicate names; `typ` is `JWT`, no
   `crit`, a `kid` if any is `#atproto`. `iss` is checked here rather than by the SDK's `IsValidDid`, which passes
   `did:web:evil.com%2Fpath`, `did:web:evil.com%3F` and a trailing line feed, and which would otherwise send an unchecked
   token's words to the resolver: it is `did:plc:` and 24 base32 characters, or `did:web:` and a plain lowercase host with
   no path, its last label starting with a letter, with `%3A` and a port only for an origin the transport guard allows.
   Each form is anchored at the end of the string.
2. **Algorithm.** ES256 or ES256K, nothing else, and later exactly the signing key's.
3. **Audience and method.** `aud` and `lxm` equal, ordinally, what the application asked for.
4. **Times.** `exp` is in the future and at most five minutes away; `iat` before it, no more than five minutes before,
   and at most 30 seconds ahead of this clock; `nbf`, if any, has passed.
5. **Signature.** The one `#atproto` key the issuer's document controls, through `AtprotoSessions.ResolveDid`, in a
   Multikey or a 2019 key type; compact, low-S ECDSA over SHA-256 of the exact bytes received. BouncyCastle, because .NET
   does not offer secp256k1 on every platform. `exp` is read once more afterwards, since resolving took time.

A refusal is an outcome, never an exception, so a consumer maps it once. `IssuerUnreachable` is kept apart: it says nothing
about the token, so a new attempt can pass. A directory that answers that it has no such DID is `IssuerUnknown`.

The answer carries the handle the DID's document names when that handle resolves back to it, as a sign-in does, through
the check the sign-in now shares. `SignIn` then ends as the callback of a browser's sign-in ends: the same claims, the same
external identity linked, and the same cookie through `SignInAsync`, so every sign-in contributor, the session's lifetime
and its sign-out are the ones a browser has. `WebDid` derives the did:web a service answers as from its base URL, so an
application states its audience as a fact of its address, and never from a request's `Host`, which a caller chooses.

## Consequences

- The connector takes a dependency on BouncyCastle.Cryptography.
- A forged token whose claims pass spends one DID lookup and writes nothing.
- What a proved DID may do stays the application's. The connector proves who and signs them in; it issues nothing else.

### Named, not chased

- **Replay.** A token can be presented again until its `exp`, usually about a minute. A token is addressed to one service
  and method, and stopping its theft in that minute would take a row per token for a case a stolen session cookie already
  outruns, so nothing is kept. An application that needs one use claims `jti` itself.
- **The provider-qualified subject.** The cookie's sign-in event reads the provider from the callback's path, so a session
  from a token carries no `koan_provider_subject`. An `Operators` entry that names `atproto:{did}` does not match it, which
  fails closed; such an operator signs in through a browser.
- **Issuer fragments.** A service identity such as `did:web:labeler.example#atproto_labeler` in `iss` is refused as
  malformed. Accounts have no fragment, and labelers are a later need.
- **Key caching.** Every verification resolves the document, so a rotated key takes effect at once and a burst of tokens
  costs a burst of lookups. A cache would need its own expiry and its own rotation story, and nothing measured asks for
  one.
