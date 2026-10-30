# AT Protocol connector mechanics

Web Auth owns provider election, return URL policy, sign-in contributors, cookies
and external identity management. This connector implements `IAuthProtocol` and
registers the ordinary named ASP.NET scheme `atproto` plus its metadata controller
through one `AtprotoAuthModule`. No AT branch is added to the generic controller.

The native dependency is CarpaNet.OAuth/CarpaNet 1.1.0-alpha.5, whose package NuSpec
names source commit `a24d54bf6a9ce3bbf7c1961d37ab099abe1d1a65`. The SDK supplies
identity parsing/resolution, PKCE, PAR, token exchange, DPoP key generation/proofs,
nonce storage and token refresh. The adapter adds the checks
and lifecycle required to fit the AT profile to ASP.NET sign-in.

DNS TXT resolution uses maintained DnsClient 1.8.0 over TCP via the SDK resolver
hook, with matching answer names; the SDK's raw UDP parser is not used. Revocation
uses SDK DPoP proof generation and nonce state, includes the required client_id,
checks the provider response and retains local state on failure. This corrects
the pinned SDK's best-effort revoke method, which otherwise hides rejection.
The adapter rejects SDK non-PAR fallback and enforces the configured permission
ceiling on both staged token responses and restored/refreshed sessions. Callback
grants must additionally contain every exact scope in the protected request;
downscoped grants are rejected before either durable or cached session replacement.
An ordinary default challenge reuses an existing broader durable scope set only
after the same DID/PDS/issuer/client and current declared ceiling validate;
explicit scope challenges retain their exact requested set.

The client identity is `Providers:atproto:ClientId` when configured, else it is
derived at use from the application's base URL (WEB-0078, WEB-0081):
`Koan:Web:ApplicationUrl` when set, else the URL the application accepted, else,
where Koan derives the base URL from requests (Development, Staging/Test with a
notice, Production under the application's `DerivesUntilAccepted`), the current
request's. An https base yields `{base}/auth/atproto/client-metadata.json`; a
loopback http base yields the localhost profile in Development only. A challenge,
handle-first or server-first, reads the identity once and uses it for the pushed
request, its `redirect_uri` and the authorization URL. The callback reads the
identity current at the callback and uses it for the code exchange and the session
it saves, so a sign-in in flight across an accept fails closed at its callback (the
code was issued to the previous identity) and the person signs in again. Restored clients are cached per DID together
with the identity they were built for; a cached client built for another identity is disposed and rebuilt.

The identity is checked at composition. A configured base URL is checked there as
it always was. With nothing configured, the check passes when Koan derives the base
URL from requests, and, when the application registers an `IApplicationUrlStore`,
it waits for the store: `AtprotoAuthModule.Start` (after `WebModule`) runs it on the
URL the store held, and an eligible provider with no identity it can publish refuses
the start with the same correction. A stored value Koan.Web does not use here (http
loopback kept from Development, started in Production) is nothing held: without
`DerivesUntilAccepted` the start is refused as for an empty store, with it the
identity derives from the request until the application accepts a URL.

A stored session saved under another client identity — the application accepted a
new URL since — is gone. Requests and `GetSessionMetadata` treat it as absent; a
default challenge does not reuse its scopes and a server-first callback does not
preserve it, so the new grant replaces it; `Disconnect` revokes it in the old
identity's name as a best effort (a provider that authenticates public clients by
their metadata document may refuse, since the application no longer publishes that
identity) and removes it either way. It stays stored until one of those happens, so
accepting the previous URL again restores it.

1. The standard challenge receives a handle/DID and AuthenticationProperties.
   The remote handler creates its browser correlation cookie and protects those
   properties separately from SDK state.
2. Guarded discovery resolves the DID/PDS/authorization issuer. A local handle map
   is used only under the explicit development gate and requires the reverse DID
   binding. A public handle uses normal SDK resolution.
3. SDK state contains the expected DID, PDS, issuer, requested scope and protected
   browser properties. SDK PKCE/PAR creates the authorization URL.
4. Callback processing validates browser correlation before exchanging a code.
   It requires one state and issuer, exact issuer equality, expected token subject,
   and a fresh subject/PDS/issuer resolution.
5. SDK session writes go to a `SessionCapture`, which persists nothing, until those
   checks and exact requested-scope coverage pass. A missing requested permission
   produces a sanitized HTTP 400 correction without touching the previous session.
   Only then is the grant committed to the DID's row, conditional on the row's
   version as read, and its client admitted to the cache. An identity-only sign-in
   that finds a validated broader grant keeps it; a row that changed between the
   read and the commit is read and decided again, a bounded number of times, so an
   in-flight refresh never overwrites a newly authorized session and a sign-in never
   overwrites a grant it did not see.
6. The principal carries protocol DID/optional verified handle claims, links the
   existing external-identity store, and enters Koan's cookie lifecycle. No OAuth
   credential is placed in AuthenticationProperties tokens.
7. A step that fails throws `AtprotoSignInException` carrying the Web Auth
   `SignInFailure` for its phase (WEB-0080): identity resolution is
   `HandleUnresolved`; discovery, metadata and the pushed request are
   `ProviderUnreachable`; an unknown state, a failed correlation or an issuer
   mismatch is `CallbackMismatch`; a callback answering our state with
   `error=access_denied` is `AccessDenied`. Both the challenge and the callback ask
   `SignInFailureReturn` first and send the person to the application's landing
   with the code when one is named; otherwise the connector's own 400 answers.

## Sessions and pending authorizations

`AtprotoSessionStore` owns two Entity rows in the application's store, both host-wide
(`IAmbientExempt`), so an anonymous callback reads them under any tenancy. Nothing is held in
the process, so every process over one store and key ring behaves alike (WEB-0082); the specs
stand two connector instances over one Koan host for two processes, since Koan's Entity statics
are per process.

- **`AtprotoAuthorizationState`** — one row per pending authorization. Its id is the SHA-256
  of the state, so the state itself is never stored; `Pending` is the protected PKCE verifier,
  DPoP key and binding; `ExpiresAt` is indexed. `Peek` is one keyed read and answers only a live
  row. `ConsumeAsync` reads the row and deletes it by id: of concurrent consumers, the one whose
  delete removed the row receives it. A state that is not well-formed UTF-16 names no row.
- **`AtprotoSession`** — one row per DID, its id the SHA-256 of the DID and `Did` checked
  ordinally on read. `Grant` is the protected `OAuthSessionData`; `ExpiresAt`, indexed, is the
  last grant write plus 14 days, the longest a public client's refresh token lives;
  `ClaimedUntil` and `ClaimId` mark a grant one process holds to refresh or revoke. `Version`
  is a fresh random value on every write, never reused. A commit, a claim and the sweep are
  conditional on the version read; a claim holder's renewals, release and end are conditional on
  its `ClaimId` instead, and every other write leaves the row without it. A refresh's result is
  conditional on the protected `Grant` it refreshed from, which only a sign-in, a result or an
  end writes anew, and clears whatever claim holds the row. A store that cannot replace
  conditionally is refused at start for an
  eligible provider. Ending a session is a conditional delete where the store declares one, and
  otherwise a conditional replace that empties the row to its DID and expires it at once. A
  write under a claim runs until the store answers or its deadline passes, and is cancelled
  then; past it, its outcome is unknown, and the fences settle whatever a store that ignores
  cancellation lands late.
- **Reading.** `Read` is one keyed read. A grant that cannot be unprotected or deserialized, or
  that names another subject, is logged and read as no grant for that DID alone; the next
  sign-in commits over its version. A grant past `ExpiresAt` reads as none.
- **Claims.** `SessionClaim` takes a new `ClaimId` and a lease of 15 seconds. A renewal falls
  due 5 seconds after the lease last landed started, on its own task, and lands only while the
  row carries the claim under an earlier `ClaimedUntil`, so a renewal only ever extends the
  lease. While the latest has not answered, another starts every third of an interval
  (`AttemptSpacing`, 1.67 s with the defaults); none is cut off before the end of the lease it
  serves. A refused renewal is read: still under the claim, a newer renewal superseded it; without
  it, the claim is lost. Renewals never wait behind the result write, go on while a provider
  request is in flight, and stop once the claim is settled or lost, or once its lease ended with
  none landed. A holder that stops keeps others waiting no longer than the lease. Disposing a
  claim that is not settled releases it, clearing `ClaimId` and `ClaimedUntil`, within the lease
  it starts in or an interval when less is left.
- **Refresh.** A request whose stored access token is due within 30 seconds claims the row and
  starts its refresh only with half a lease left and the row, read again, still under its claim
  with the grant it was taken on (`Begin`, `RequestRoom`); with less room, it waits an interval
  for a renewal to give it, and otherwise releases the claim and reads the row again, until the
  wait bound. The refresh, once sent, runs to its answer whatever the caller's cancellation, since
  the provider rotates the refresh token as it answers. Its result is stored while the row holds
  the grant it refreshed from, and clears the claim in the same write — a takeover's too — so no
  renewal or release, the holder's or a taker's, can land after it. That write keeps one version
  across its attempts, a third of an interval apart within a window of a lease; a refused attempt
  reads the row, so one an earlier attempt landed is recognized. When none is known to have
  landed, the claim is released and the row read once every attempt has answered, or an interval
  on: a row holding this result means it was stored; one holding another grant is read again;
  while an attempt is still pending in the store, or when the row cannot be read, the outcome is
  unknown, logged at Warning, and the request fails with `TimeoutException`; otherwise the row id
  is logged at Error and the request fails. Others wait, pausing from 50 ms up to 1 s, for the
  stored grant or for the claim to lapse, for one request timeout at most; then the request
  fails with `TimeoutException`, logged at Information, having written nothing. A waiter takes
  over only once the lease lapses, which a working holder prevents for as long as it works: two
  request timeouts and `ClaimHold` (an interval and three leases), 110 s with the defaults. A
  provider that answers `invalid_grant` has ended the grant: the row ends and the person signs in
  again. Any other failure releases the claim and keeps the session. A result refused means the
  row is read again and the client retired. Retryable failures throw `TimeoutException`; a
  session that must be signed in again throws `InvalidOperationException`.
- **Disconnect.** The client and the issuer's revocation endpoint are read before the claim, so
  the claim covers the revocation request and its nonce retry alone, started as a refresh is. The
  row ends under the claim's fence only after revocation is confirmed, and a disconnect whose end
  is refused throws rather than reporting success.
- **Residuals.** WEB-0082 §3 names what the fences cannot close: a holder taken over while its
  request is at the provider, whose late result clears a taker that already sent its own refresh,
  a store whose writes take more than half a lease, which serves no refresh until it speeds up,
  an answer lost after the provider rotated, and process clocks that differ.
- **Sweep.** `AtprotoSessionSweep`, a hosted service, runs `Sweep` at start and every ten minutes
  while the provider is eligible: expired pending authorizations and expired session rows —
  ended or not refreshed in time — are deleted, 100 rows per read, each pass stopping at a short
  page or a page that removed nothing. A session row is deleted conditionally on its version
  where the store allows it; elsewhere it is read again by its key and deleted only if it is
  still that version, which leaves the window WEB-0082 names. Every process may sweep.

## Clients

A restored or signed-in SDK client is cached per DID in a `ClientCache` of `CachedClients`
entries. Admitting one past capacity evicts the client used least recently and disposes it;
the cache never empties itself and never refuses. An operation rents a client and returns it;
one evicted, replaced or retired while rented is disposed when its last renter returns it. A
client is reused only while the row still holds the protected grant it was built from and the
client identity it was built for; otherwise it is retired and rebuilt from the row, resolving
and checking the identity again. A send that fails retires its client. Requests of one DID run
concurrently on one client: the SDK's nonce cache is concurrent, a DPoP proof signs with a key
nothing changes, and a refresh is serialized by the row's claim.

Restoration checks subject, PDS, issuer and the current client identity again. PDS origin
comparison permits the SDK's equivalent trailing slash after refresh; callback
issuer comparison remains exact.

## Transport

`AtprotoHttp` checks scheme and origin on every outgoing request and resolves DNS
inside `SocketsHttpHandler.ConnectCallback`. It rejects private, loopback,
link-local, documentation, multicast, mapped and selected translation addresses,
then connects the specific vetted address while the standard handler retains TLS
hostname verification. Public destinations require HTTPS/default port. Redirects
and ambient proxies are disabled. Two clients share that handler. The protocol client the
SDK and the connector read discovery, identity, pushed-request, token and revocation
documents through buffers each response and refuses one longer than
`MaximumProtocolResponseBytes`, within `RequestTimeoutSeconds`. `Send`, for XRPC and for a
caller's own request, returns once headers arrive within `RequestTimeoutSeconds` and streams
the body under the caller's cancellation: with a limit, a declared `Content-Length` over it is
refused before reading and a body that runs past it fails the read that crosses it, after at
most one byte beyond the limit; without one, the caller streams it.
The initial conservative address policy is not a general networking framework.
Exact development origins are allowed only by the named environment gate.

The development PLC selector applies only to explicit `DevelopmentHandles` keys
or DID values and is used consistently for challenge, callback, restore and DID
document lookup. Unknown/public identities use `PlcDirectory`; lookup failure
never triggers a fallback to another directory. `DevelopmentConnectHost` affects
socket DNS only for an exact allowed development origin; public destinations keep
their original connection host and address checks. Production configuration
rejects both settings. These hooks let one container use disposable loopback
fixtures while continuing to resolve public identities normally.

The SDK's raw `ATProtoOAuthClient.HttpClient` is not exposed: it bypasses this
transport. Guarded XRPC composes SDK DPoP requests and sends them through the same
connection owner. Namespace syntax cannot supply a separate URL; destination
comes from the verified session. A server action still owns whether it may use a
particular DID's grant. This service is not an HTTP impersonation endpoint.

## Evidence

Evidence boundaries:

- S01 outside this module proved real OAuth, callback replay denial, native
  cross-PDS Spaces transport, and real refresh after a process restart.
- This module's focused tests exercise address guards, configuration corrections,
  development exceptions, protected state consumption, staging, permission ceilings,
  requested-grant completeness and mandatory PAR, and, over a SQLite store with a loopback
  provider that rotates refresh tokens and ends a grant whose rotated token is replayed, two
  connector instances standing for two processes: signing in at once and reading each other's
  sessions, one refresh of a DID across both, a stopped holder's claim taken over within its
  lease, the claim interleavings (a claim lost mid-refresh, a renewal unrecognized past its
  bound, loss at the mark, a store cut with a second process sending, a stale renewal after an
  unrecognized result, a result write stalled within and past its bound, a renewal hung through
  a short outage, a result that outlasts an outage, a result stored though its release was
  refused), a refreshed write that fails once still landing,
  a row this application cannot read failing only its DID, a sign-in replacing a claimed grant,
  a provider-ended grant, a checked disconnect and one that ended nothing, the paged sweep,
  pruning on DuckDB, which deletes nothing conditionally, the client cache's bound, eviction
  and disposal, and streamed responses at every limit from 0 to `long.MaxValue` (WEB-0082). The
  destructive provider lifecycle test is skipped unless explicitly enabled. The generic
  sample additionally passed real correlation/issuer/state/replay, cookie sign-in and actual
  process-restart checks. Redacted receipts are in `evidence/`.
- The opt-in local-provider lifecycle proof opens the stopped sample's own store and key
  ring. It expires the stored access token's metadata to cause a real refresh, verifies
  rotation of both tokens and authenticated `getSession`, then proves checked revocation
  makes the retained refresh token fail with `invalid_grant`. Its receipts in `evidence/`
  predate session rows. It never exports credentials.
- Processes sharing one store are proven as connector instances in one process, against the
  loopback provider, not as separate processes and not against a public provider.
  No production certification, confidential-client lifetime, A2A/MCP/WebMCP support, or
  Spaces repository verification is implied.
- The public repo MST parser in this SDK cannot verify the current experimental
  Spaces commitment/index format. That is a separate protocol data concern.

Follow-up SDK contributions should cover callback issuer/token-subject checks and
binding validation explicitly. This adapter must not remove its guards merely
because a future SDK adds similarly named methods; rerun the negative evidence.

Server-first entry (2026-09-12): a challenge without `identifier` starts at
`https://bsky.social`. This follows https://atproto.com/specs/oauth#identity-authentication:
bind issuer, omit login_hint, then resolve token subject to DID -> PDS -> issuer.
CarpaNet alpha.5 incorrectly adds URL inputs as login hints and retains the entryway
as its token audience. The connector uses its PKCE/DPoP primitives for the initial
mandatory PAR and lets the staged session store resolve/check the subject and set
the actual PDS before SDK client construction. Existing correlation, one-use state,
permission ceilings, callback issuer binding and guarded transport remain in force.
ProtocolGuards includes a real loopback PAR nonce exchange with no login hint and
negative subject/issuer/PDS binding checks. No completed public-provider callback is
claimed by that fixture.
