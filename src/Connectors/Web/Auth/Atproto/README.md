# Sylin.Koan.Web.Auth.Connector.Atproto

Native AT Protocol sign-in and authorized sessions for Koan Web Auth. The connector uses
CarpaNet.OAuth 1.1.0-alpha.5 and claims only the documented public-client flow and boundaries.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth.Connector.Atproto
```

Keep the application bootstrap ordinary: reference the package and call
`builder.Services.AddKoan()`; do not register an authentication scheme or callback manually.
The connector keeps its sessions as rows in the application's own store, so reference a Koan
data connector that replaces a row conditionally — SQLite, PostgreSQL, SQL Server and MongoDB
do. An eligible provider over a store that cannot refuses to start and names the correction.

## Meaningful use

Keep `PlcDirectory` at the public default when accepting public accounts. A mixed
development network can set `DevelopmentPlcDirectory` for only the handles/DIDs
explicitly listed in `DevelopmentHandles`. For Docker Desktop, set
`DevelopmentConnectHost` to `host.docker.internal` to reach the exact origins in
`DevelopmentAllowedOrigins` through the host. This changes only the socket
destination; issuer URLs, HTTP Host, TLS identity and DPoP audiences stay intact.
Neither setting is accepted outside Development. Failed discovery or PAR sends
the person to the application's failure landing when one is named (below), and
otherwise returns a sanitized HTTP 400 retry page, without exposing the provider
response.

Declare the application's public URL once; the client-metadata URL and the callback derive from it
(WEB-0078):

```json
{
  "Koan": { "Web": {
    "ApplicationUrl": "https://your-app.example",
    "Auth": {
      "Providers": { "atproto": { "Scopes": ["atproto"] } }
    }
  }}
}
```

With `ApplicationUrl` set to `https://your-app.example`, the connector serves and advertises
`https://your-app.example/auth/atproto/client-metadata.json` and uses the callback
`https://your-app.example/auth/atproto/callback`; under `https://your-app.example/app` both sit under
`/app`. A loopback `http://localhost:<port>` base URL derives the AT Protocol localhost development
profile, in Development only. `Providers:atproto:ClientId` remains an explicit override of the metadata
URL, which must still end in `/auth/atproto/client-metadata.json`. Without `ApplicationUrl`, the URL the
application accepted at runtime stands in for it and is read at use, so accepting another moves the client identity
on the next request (WEB-0081); with neither, the identity derives from the request where Koan derives the base URL
(Development, and Staging, Test and CI with a notice), and in Production only under the application's
`DerivesUntilAccepted` posture, until it accepts its URL. A sign-in in flight and a
stored session were granted to the previous identity and end when it changes; the person signs in again. This first
connector uses the public OAuth client profile, with no client secret or confidential-client key; provider session
lifetime limits still apply.

Send the browser to `/auth/atproto/challenge?return=<path>` for Bluesky account
selection on its own authorization page. Keep
`/auth/atproto/challenge?identifier=<handle-or-DID>&return=<path>` as the alternative
for other providers or a specific account.
Koan validates the return destination and creates its ordinary authentication
cookie after the connector verifies correlation, issuer, account DID and PDS.

Name where a failed sign-in lands beside `return` — `&failure=/sign-in`, a rooted
path of the application in printable ASCII, with no `.` or `..` segment, no encoded
slash and no `error` of its own, placed under its base path — or for every provider in
`Koan:Web:Auth:ReturnUrl:FailurePath` (WEB-0080). A sign-in that cannot start or
cannot be verified then sends the person there with one code in the query, never
the provider's text: `/sign-in?error=provider_unreachable`, `handle_unresolved`,
`access_denied` (the person refused), `callback_mismatch` or `sign_in_failed`.
With nothing named the connector answers its own HTTP 400.
Use `AtprotoClaimTypes.Did` for protocol identity even if a local identity module
reconciles `ClaimTypes.NameIdentifier`. `AtprotoClaimTypes.Handle` is optional and
is emitted only after bidirectional handle verification. Neither email nor a
Bluesky profile is requested.

Server-first sign-in binds the authorization server before the account is known.
The returned DID is resolved to its PDS and authorization server before accepting
the session. An identity-only sign-in keeps an existing validated broader local
grant rather than overwriting room access with the new identity-only grant.

## Request additional access deliberately

For this connector, `Providers:atproto:Scopes` declares the client-metadata upper
bound. A first ordinary sign-in requests only `atproto`. When a validated durable
session for the same DID/PDS/issuer/client already has a broader grant within
the current ceiling, ordinary sign-in requests that established grant again.
A trusted server action requests additional exact declared scope strings:

```csharp
var properties = new AuthenticationProperties { RedirectUri = "/approved-return" };
AtprotoChallenge.WithScopes(properties, "atproto", "your-exact-declared-scope");
return Challenge(properties, "atproto");
```

Validate the return destination in an application-owned challenge action. Raw
query-string scopes do not change permissions. The requested set must include
`atproto` and be an exact subset of the declared strings. Declare separate
ordinary and management permission strings; request management only from the
server action authorized to establish that grant. OAuth scopes grant PDS access,
not application administration roles.

The callback requires every requested scope in the returned token grant and
rejects any scope outside the request or current declared ceiling. A provider
that silently omits requested access receives a sanitized HTTP 400 correction;
the previous durable session and cached client are retained. Refresh and
restoration check the current declared ceiling without requiring optional
permissions that were never requested. Explicit requested scopes always remain
exact. This first connector
accepts exact scope strings; it does not interpret permission-set expansion or
experimental Spaces scope defaults. Supply concrete resource scopes in their
provider-canonical form. For the pinned experimental Spaces provider, specify
the authority DID and collections explicitly: `authority=self` and omitted
collections are rewritten during token issuance and therefore fail the exact
grant check. Reauthorization is required when the client ID or permission ceiling
changes.

`AtprotoSessions` in the `.Protocol` namespace sends XRPC through the native
session using `Send(did, nsid, method, maximumResponseBytes, parameters, content, ct)`.
It fixes the destination to that DID's verified PDS, applies SDK DPoP and refresh, and
returns a disposable `HttpResponseMessage` as soon as its headers arrive. The body
streams: the caller reads no more than `maximumResponseBytes` of it, or all of it when
that is `null`, for a caller that streams a blob or a long page. A body that declares
a longer length is refused before it is read; one that runs past the limit fails the
read that crosses it. `Disconnect(did)` revokes the upstream grant. Ordinary Koan
logout clears only the local cookie.

```csharp
using var page = await sessions.Send(did, "com.atproto.repo.listRecords", HttpMethod.Get,
    maximumResponseBytes: 1024 * 1024, parameters: query, ct: ct);
using var blob = await sessions.Send(did, "com.atproto.sync.getBlob", HttpMethod.Get,
    maximumResponseBytes: null, parameters: blobQuery, ct: ct); // streamed by the caller
```

For a protocol workflow that uses a different credential,
`AtprotoHttp.Send(request, maximumResponseBytes, ct)` reuses the same network guards and
the same streaming limit while the caller owns headers and credential context.
`AtprotoSessions.ResolveDid(did, ct)` returns the SDK DID document after an exact
identifier check. Neither API exposes stored OAuth secrets or the raw HttpClient.

## Guarantees and boundaries

### Custody and processes

Each DID's session — its tokens, DPoP key and the client identity it was granted to — is
one row of the application's store (WEB-0082), and each pending authorization is one row
until its callback consumes it or it expires. Every row is protected on its own with the
ASP.NET Data Protection key ring, so a backup of the database carries the sessions, and the
key ring must persist beside it: set `Koan:Web:KeyRingDirectory` to a folder on persistent
storage (WEB-0079), so a rebuilt container, or a renamed application, still reads the
sessions it protected. Token and key material is never placed in the browser cookie.
Protect the key ring for the deployment account (the folder's permissions, or an encryptor
added through ASP.NET's `IDataProtectionBuilder`).

Nothing about a session is held in the process, so every process over the same store and
key ring can sign people in and read every session; the connector's specs prove this with
two connector instances over one store in one process, not with separate processes
(WEB-0082). A grant due for refresh is claimed on its row with a short lease its holder
renews while it works, fenced by a claim identity, and the refreshed tokens are stored only
over the grant they replace, so one process refreshes it and the others use the tokens it
stores; a holder that stops keeps them waiting no longer than the lease. WEB-0082 names what
this cannot close: a holder cut off from the store for longer than the lease while its
refresh is at the provider, and an answer lost after the provider rotated the token. A row this
application cannot read — its key was lost — is that one DID's session gone: the person
signs in again, and the new grant replaces the row. Sessions not refreshed for 14 days,
the longest a public client's refresh token lives, ended sessions and expired pending
authorizations are swept a page at a time every ten minutes.

`Koan:Web:Auth:Atproto` options are:

| Key | Default / meaning |
| --- | --- |
| `PlcDirectory` | `https://plc.directory` |
| `MaximumProtocolResponseBytes` | 4194304, the largest discovery, identity, pushed-request, token or revocation document the connector reads itself |
| `RequestTimeoutSeconds` | 30; a sent request's headers arrive within it, and so does each protocol request a refresh or revocation makes |
| `CachedClients` | 1024 signed-in clients one process keeps ready; past it the one used least recently is closed |
| `DevelopmentAllowedOrigins` | Empty; exact HTTP(S) origins for a local protocol test network |
| `DevelopmentHandles` | Empty; explicit local DNS fixture map from handle to DID |

Development exceptions require `KoanEnv.Gate.DevelopmentOnly`; no production
override exists. A fixture handle still resolves through the real DID directory
and must appear in that DID document's `alsoKnownAs`. Normal public handles use
SDK DNS/HTTPS resolution. The special `http://localhost` client ID must contain
one IP-loopback `redirect_uri` with the canonical callback path and one `scope`
query value equal to the declared upper bound. See `samples/AtprotoIdentity`.

An inactive reference supplies availability without initiating network traffic or
touching session storage. Incorrect client/issuer/endpoint settings fail with a
corrective configuration message. Failed browser verification returns a generic
HTTP 400 without disclosing provider responses or token material.
