---
id: WEB-0082
slug: at-sessions-are-rows-in-the-applications-store
domain: Web
status: Accepted
date: 2026-10-30
title: AT sessions are rows in the application's store, refreshed by one process at a time
amends:
  - WEB-0079
related:
  - WEB-0078
  - WEB-0081
  - SEC-0030
---

# WEB-0082: AT sessions are rows in the application's store, refreshed by one process at a time

## Context

The AT Protocol connector kept every person's session and every pending authorization in one encrypted file,
`protocol.protected` under `AtprotoOptions.SessionDirectory`, behind `owner.lock`, opened with `FileShare.None` for the
life of the process. Each sign-in, pending authorization and refresh rewrote and re-encrypted the whole file under one
process-wide lock, so every write cost the size of every session; sessions never expired; and a second process over the
same folder could sign no one in — a second replica, a blue/green switch or an overlapping deploy. A file the key ring
could not unprotect left the lock held and the data unread, so every later call failed until restart. A consumer had to
point the folder at its data volume and teach its backup to skip the lock.

Beside it, the per-DID client and lock maps (`ConcurrentDictionary`) grew with every DID seen since start, and a failed
send dropped its client without disposing it. And the 4 MB cap meant for discovery and server-side request forgery
applied to every XRPC response through `Send`, which buffered the whole body: a person's blob or a long page could not be
read.

Measured on the unchanged code at `1dbea23` (Koan.Web.Auth.Atproto.Tests, a loopback PLC directory, PDS and
authorization server that rotates refresh tokens and, like the AT Protocol reference provider, ends a grant whose
rotated refresh token is presented again):

- Two `AtprotoSessions` over two stores on one folder, signing two DIDs in at once: the first failed with
  `IOException` (`owner.lock` in use); its reads of the other DID failed the same way.
- Two `AtprotoSessions` over one store object, one DID whose access token had expired, sending at once with the refresh
  answer delayed 300 ms: the provider received two refreshes, the second a replay; one send got 401, the other
  `TokenRefreshException` (`invalid_grant`, refresh token replayed), and the stored refresh token belonged to a grant
  the provider had ended.
- The file protected under another key ring: reading any DID threw `CryptographicException`; every later call, a new
  pending authorization included, threw `IOException` on `owner.lock`.
- After 40 DIDs signed in and sent, the maps held 40 clients and 40 locks. A send whose connection dropped removed its
  client undisposed.
- A 5 MB XRPC response through `Send` failed with "AT Protocol response exceeded the configured byte bound."

## Decision

**A session is one row per DID and a pending authorization one row per state, in the application's own store, each
protected on its own. Every write to a session is conditional on the version it read; a refresh is claimed on the row,
so one process makes it. Reads are one keyed read or a bounded page, and nothing is held in the process.**

### 1. Rows

- **`AtprotoSession`**: `Did`; `Grant`, the `OAuthSessionData` protected with the Data Protection key ring;
  `ExpiresAt`, indexed; `ClaimedUntil`, for the claim that holds it; and `Version`. Its id is the SHA-256 of the DID, so a store that compares ids
  without case never merges two DIDs, and the DID read back must equal the asked one ordinally.
- **`AtprotoAuthorizationState`**: `Pending`, the protected PKCE verifier, DPoP key and binding, and `ExpiresAt`,
  indexed. Its id is the SHA-256 of the state; the state is never stored.
- Both are `IAmbientExempt`: a person's AT session is host-wide, and the callback that reads a pending authorization is
  anonymous.
- `SessionDirectory`, `ProtectedAtprotoStore` and `VerifiedSessionStore` are removed. **No migration:** an existing
  session file is not read, and people sign in again. The folder and its `owner.lock` can be deleted.

### 2. Every session write is conditional

`Version` takes a fresh random value on every write. A sign-in's commit, a claim, a release, an end and the sweep replace
or delete the row only while `Version` still equals the value read or written (`ReplaceIf`, `DeleteIf`); a refresh's
result is conditional on the grant it refreshed from instead (§3). A random version is never reused, so a guard can never
match a newer state. A first sign-in inserts only if the row is still absent where
the store inserts conditionally; elsewhere, of two first sign-ins of one DID at once, the later stands. A sign-in
decides whether to keep a validated broader grant on the row as read and commits only if it is unchanged, reading again
a bounded number of times. The connector requires conditional replacement: an eligible provider over a store that cannot
refuses to start, naming the correction. Ending a session is a conditional delete where the store declares one, and
otherwise a conditional replace that empties the row to its DID and expires it at once, so the next sweep removes it;
neither ever removes a grant written after the one read.

### 3. One process refreshes a grant, under a fixed lease

A grant whose access token is due within 30 seconds is claimed on its row by one conditional write on `Version`, which
sets `ClaimedUntil` to now plus a fixed lease: two request timeouts, for a refresh or a revocation and its nonce retry,
and ten seconds for the write that settles the claim. With the defaults, 2 × 30 s + 10 s: 70 s. Nothing renews it, and no
other write happens under it, so the row's version is the claim's identity.

- **The holder makes its one request.** A request once sent runs to its answer, whatever the caller's cancellation,
  since the provider rotates the refresh token as it answers.
- **The result lands on the grant.** It is conditional on the row still holding the protected grant the refresh
  presented, and it clears `ClaimedUntil` in the same write. The protected grant serves as the grant's version: it is
  written anew by every sign-in, result and end, and copied unchanged by every claim and release. So a result that lands
  late still lands over a process that took the claim, and no result lands over a newer grant: a sign-in's, an end, or
  another holder's result. A refused result means the row holds another grant, and the row is read again.
- **Failure clears the claim.** A provider that answers `invalid_grant` has ended the grant, and the row ends with it,
  conditional on the claim's version. Any other failure releases the claim, conditional on the same version, and keeps
  the session.
- **Everyone else waits, briefly.** Every other request for that DID, in any process, reads the row again every 250 ms
  for the stored grant, for at most one request timeout, 30 s with the defaults; then it fails with `TimeoutException`,
  to be tried again, having written nothing.
- **An expired claim is simply taken.** A holder that stopped keeps others out no longer than the lease.
- **A store too slow or down fails the request.** Each write that settles a claim has a bound of ten seconds. A result the
  store did not take fails its request with `TimeoutException`, logged at Warning with the row id, never a token; the
  client that holds the rotated tokens stays in the process's cache, and the next request there stores them with no
  provider request.

`Disconnect` builds the client and reads the issuer's revocation endpoint first, then claims the grant, so the claim
covers the revocation request and its nonce retry alone. It ends the row, on the claim's version, only once revocation is
confirmed. A disconnect whose end is refused — the person signed in again meanwhile, another process took the claim, or a
late result landed — throws, saying the session was not ended; it never reports success over a row it did not end.

**What this accepts.** These are rare, and each costs at most one person one sign-in. Closing them would burden every
session with machinery out of proportion to them.

- A holder that stops or outlasts its lease mid-request can be followed by a taker that presents the same refresh token;
  a provider that ends replayed grants then ends the grant. Only fencing at the provider closes this.
- A result the store did not take is lost when the process holding it stops, or when another process refreshes first:
  that process presents the rotated-away token, the provider refuses it, and the person signs in again.
- A request whose answer is lost after the provider rotated the token — a connection error after sending — leaves the
  stored refresh token rotated away. No client can tell that case apart; the next refresh is refused, the row ends, and
  the person signs in again.
- Clocks: a waiter takes an expired claim by its own clock against the `ClaimedUntil` the holder wrote by its clock. The
  lease's margin over a refresh that typically settles in under a second tolerates ordinary skew.

The SDK writes sessions only to a `SessionCapture`, which persists nothing: a sign-in's first write is verified and held
until the browser, DID, PDS, issuer and grant checks pass; a refresh's write is held until the claimer stores it.

### 4. Failure stays with its DID

A grant that cannot be unprotected or deserialized, or that names another subject, is logged and read as no session for
that DID: requests answer "AT authorization is required for this DID.", `GetSessionMetadata` answers null, and the next
sign-in commits over the row's version. Nothing is locked and no other DID is touched. A row is never deleted for being
unreadable, so a replica with the wrong key ring cannot destroy what another can read.

### 5. Expiry

A session's `ExpiresAt` is its last grant write plus 14 days, the longest the AT Protocol OAuth profile lets a public
client's refresh token live; a grant past it reads as none. A row emptied when its session ended expires at that moment.
`AtprotoSessionSweep` deletes expired pending authorizations and expired session rows at start and every ten minutes,
while the provider is eligible, reading 100 rows at a time; a pass stops at a short page or at a page that removed
nothing, so a sweep always finishes. Every process may sweep. Where the store deletes conditionally, a row is deleted
only if it is still the version read. Where it does not, the row is read again by its key and deleted only if it is
still that version, and one window remains: a sign-in that replaces the row between that read and the delete is
deleted with it, and the person signs in again.

### 6. Clients are a bounded cache

Restored and signed-in SDK clients are cached per DID in `ClientCache`, sized by `AtprotoOptions.CachedClients` (1024),
which sizes memory only. Admitting a client past capacity evicts the one used least recently and disposes it; the cache
never empties itself and never refuses. An operation rents a client; one evicted, replaced or retired while rented is
disposed when its last renter returns it. A client is reused only while the row still holds the protected grant it was
built from and the client identity it was built for (WEB-0081). A failed send retires its client. The per-DID lock map
is gone: requests of one DID run concurrently, and the row's claim serializes refresh.

### 7. A caller sets the limit of what it reads

`AtprotoOptions.MaximumResponseBytes` becomes `MaximumProtocolResponseBytes` and bounds only what the connector reads
itself — identity and authorization-server discovery, the pushed request, token and revocation answers — buffered as
before. `AtprotoSessions.Send` and `AtprotoHttp.Send` return once headers arrive, within the request timeout, and stream
the body: no more than the caller's `maximumResponseBytes`, or all of it when that is null. A declared length over the
limit is refused before reading; a body that runs past it fails the read that crosses it, for every limit from 0 to
`long.MaxValue`. No body is buffered by the connector.

## Compatibility

- `AtprotoOptions.SessionDirectory` is removed; a configuration still naming it is ignored, and code setting it no longer
  compiles. Session files are not read; people sign in again.
- `AtprotoOptions.MaximumResponseBytes` is renamed `MaximumProtocolResponseBytes` and no longer bounds `Send`.
  `AtprotoOptions.CachedClients` is added.
- `AtprotoSessions.Send(did, nsid, method, parameters, content, ct)` becomes
  `Send(did, nsid, method, long? maximumResponseBytes, parameters, content, ct)`, and
  `AtprotoHttp.Send(request, ct)` becomes `Send(request, long? maximumResponseBytes, ct)`; both stream.
- The connector references `Koan.Data.Core` and needs a data connector that replaces rows conditionally.
- A backup of the application's database carries the sessions; the key ring that protects them must persist beside it.
- Requests of one DID are no longer serialized within a process.

## Evidence

Each spec below failed before this change by the measurement above, or does not compile against the removed surface.

- `SessionRows.Two_processes_on_one_store_each_sign_a_different_did_in_at_once_and_each_reads_the_others_session`
  (SQLite store; two `AtprotoSessions` with their own stores, transports and caches stand for two processes): both sign-ins succeed, each process reads and sends for the
  other's DID, and the session and authorization tables are in the SQLite file.
- `SessionRows.Concurrent_refreshes_of_one_did_on_two_processes_keep_the_newer_tokens`: one refresh reaches the provider,
  both sends answer 200, and the stored refresh token is the one the provider accepts, with no claim left.
- `SessionClaims.A_store_that_fails_the_refreshed_write_fails_that_request_and_the_next_keeps_the_session`, for a write
  that fails before it lands and one that fails after: the send fails to be tried again, the next answers 200 after one
  refresh in all, and the stored refresh token is the provider's.
- `SessionClaims.A_waiter_takes_the_expired_claim_of_a_holder_that_stopped_and_that_holders_writes_are_refused`: with the
  holder's lease at one second, the waiter refreshes within three, and the stopped holder's end and refreshed write are
  refused.
- `SessionClaims.A_waiter_behind_a_live_claim_fails_after_one_request_timeout_and_the_holders_result_lands`: the holder's
  refresh takes three seconds at the provider; the waiter, with a request timeout of 1 s, fails with `TimeoutException`,
  and the holder's result lands with the provider's token.
- `SessionClaims.A_claim_that_settles_nothing_is_released_and_the_grant_stays`.
- `SessionClaims.A_late_result_over_a_taken_claim_stands_and_the_takers_release_is_refused`: a result from a holder whose
  lease ended lands over the taker's claim, and the taker's release writes nothing.
- `SessionClaims.Two_holders_refreshing_from_one_grant_store_one_result`, over SQLite and DuckDB: the taker's result lands,
  and the lapsed holder's, from the same grant, is refused.
- `SessionClaims.A_disconnect_whose_end_fails_says_so_and_the_new_sign_in_stands`: a sign-in during the revocation stands
  and the disconnect throws. Measured before: it reported success.
- `SessionClaims.Ended_and_expired_sessions_are_pruned_on_a_store_without_conditional_delete` (DuckDB): an ended and an
  expired session are swept a row a read and the DID signs in again. Measured before: the ended row stayed, unexpired,
  and the sweep removed nothing. DuckDB's own conditional replace failed to bind its guard and is fixed beside this
  record, with `DuckDbConditionalReplaceSpec`.
- `SessionRows.A_row_this_application_cannot_read_fails_only_its_own_did_and_the_person_signs_in_again`: the other DID
  reads and sends; the unreadable one answers absent; signing in again replaces it.
- `SessionRows.Expired_states_and_sessions_are_swept_a_page_at_a_time_and_live_ones_stay`: 23 expired states and 17
  expired sessions swept five rows a read; live ones stay; a second sweep removes nothing.
- `SessionRows.A_pending_authorization_is_read_only_while_live_and_consumed_once_across_processes`,
  `SessionRows.A_grant_the_provider_ended_ends_the_session_at_its_next_refresh`,
  `SessionRows.Disconnect_revokes_the_grant_and_ends_the_session_for_every_process` and
  `SessionRows.A_sign_in_replaces_a_session_whose_refresh_another_process_holds_and_that_refresh_is_not_stored`.
- `ClientCacheSpec`: past capacity the least recently used client is evicted and disposed; a client evicted while
  rented is disposed when its last renter returns it; a stale or retired client leaves the cache, disposed once; a
  process holds no more clients than `CachedClients`; a failed send disposes its client and the next builds another.
- `ResponseLimits`: a 5 MB response is read through `Send` with an 8 MB limit, a 5 MB limit and none; a 1 MB limit
  refuses a declared 5 MB body before reading and fails a streamed one at the limit; `AtprotoHttp.Send` reads exactly at
  its limit and fails one byte under; authorization-server metadata over `MaximumProtocolResponseBytes` still fails
  discovery. `ResponseLimits.Every_limit_from_zero_to_the_largest_reads_the_whole_body_or_refuses_it` covers null, 0, 1,
  one under, the exact length and `long.MaxValue`, declared and streamed; at `long.MaxValue` the earlier arithmetic
  wrapped and returned an empty body as success.
- The existing `ProtocolGuards`, `StaleClientIdentity`, `AcceptedClientIdentity` and `FailedSignInReturn` specs pass
  over the row store.

Not proven here: two operating-system processes rather than two connector instances over one Koan host in one
process, since Koan's Entity statics are per process; process clocks that differ; and a public provider. What §3
accepts is not closed by anything here.
