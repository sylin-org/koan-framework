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
  `ExpiresAt`, indexed; `ClaimedUntil` and `ClaimId`, for the claim that holds it; and `Version`. Its id is the SHA-256 of the DID, so a store that compares ids
  without case never merges two DIDs, and the DID read back must equal the asked one ordinally.
- **`AtprotoAuthorizationState`**: `Pending`, the protected PKCE verifier, DPoP key and binding, and `ExpiresAt`,
  indexed. Its id is the SHA-256 of the state; the state is never stored.
- Both are `IAmbientExempt`: a person's AT session is host-wide, and the callback that reads a pending authorization is
  anonymous.
- `SessionDirectory`, `ProtectedAtprotoStore` and `VerifiedSessionStore` are removed. **No migration:** an existing
  session file is not read, and people sign in again. The folder and its `owner.lock` can be deleted.

### 2. Every session write is conditional

`Version` takes a fresh random value on every write. A sign-in's commit, a claim and the sweep replace or delete the row
only while `Version` still equals the value read (`ReplaceIf`, `DeleteIf`); a claim holder's renewals, release and end
are fenced by its claim identity instead, and a refresh's result by the grant it refreshed from (§3). A random version
is never reused, so a guard can never match a newer state. A first sign-in inserts only if the row is still absent where
the store inserts conditionally; elsewhere, of two first sign-ins of one DID at once, the later stands. A sign-in
decides whether to keep a validated broader grant on the row as read and commits only if it is unchanged, reading again
a bounded number of times. The connector requires conditional replacement: an eligible provider over a store that cannot
refuses to start, naming the correction. Ending a session is a conditional delete where the store declares one, and
otherwise a conditional replace that empties the row to its DID and expires it at once, so the next sweep removes it;
neither ever removes a grant written after the one read.

### 3. One process refreshes a grant, under a lease fenced by its identity and its grant

A grant whose access token is due within 30 seconds is claimed on its row by a conditional write on `Version`, which
sets `ClaimedUntil` and a `ClaimId` new for that claim.

- **The claim identity fences renewals, the release and the end.** Each is conditional on the row still carrying the
  holder's `ClaimId`, not on the last version it saw, so a renewal it failed to recognize never costs it anything.
  Every other write leaves the row without that `ClaimId`: a takeover writes its own, and a sign-in, a refresh's
  result, an end, the sweep and a first sign-in's upsert write none. So a holder's renewal, release or end never lands
  on a row another has taken or replaced, whatever order its writes land in.
- **The grant fences the result.** A refresh's result — the rotated tokens — is conditional on the row still holding
  the protected grant the refresh presented, whichever claim holds the row then, and it always clears `ClaimId` and
  `ClaimedUntil`. The protected grant serves as the grant's version: it is written anew by every sign-in, result and
  end, and copied unchanged by every claim, renewal and release. So a result that lands late still lands over a process
  that took the lapsed claim over, and that taker's renewals and release, fenced on its own identity, are refused from
  then on and never write the old grant back; no renewal of the holder's own can either. No result lands over a newer
  grant: a sign-in's, an end, or another holder's result. A holder begins a request only after reading the row again
  and finding it still under its claim with the grant it was taken on, which is the grant its client presents, so a
  taker whose claim a late result cleared refreshes nothing. `Version` still changes with every write and guards every
  other conditional write.
- **No write is cut off short.** Every write under a claim — a renewal, the result, the release, the end — is one
  attempt that runs until the store answers or its deadline passes, and is cancelled then. A renewal's deadline is the
  lease it serves; the release's is the lease it starts in, or an interval when less is left; the result's is the end
  of its window of a lease, and the end's a lease from its start. The release, the result and the end may land after
  the lease, which the fences keep safe: the release and the end land only while no takeover happened, and the result
  only while the row holds the grant it refreshed from.
  Past its deadline an attempt's outcome is unknown; a store that ignores cancellation may still complete it, and the
  fence settles whatever it lands.
- **Renewals.** The lease is 15 seconds. A renewal falls due 5 seconds after the lease last landed started, and is one
  keyed write that only ever extends the lease: it lands only while the row carries the claim under a `ClaimedUntil`
  earlier than its own. While the latest attempt has not answered, another starts every third of an interval, so on a
  slow store attempts overlap and land in any order; with the lease three intervals long, six start before it ends. A
  refused renewal is read: a row still carrying the claim was extended by a newer renewal, and the holder carries on;
  only a row without the claim means the claim is lost. Attempts stop once the lease has ended with none landed.
  Renewals run on their own and never wait behind the result write, go on while a provider request is in flight, and
  stop once the claim is settled or lost. A holder that stops keeps others waiting no longer than the lease.
- **Room for a request.** A provider request starts only with at least half a lease left on the lease last landed.
  With less, the holder waits up to one interval for a renewal to give it that room; otherwise it releases the claim
  and reads the row again, until the wait bound, when the call fails with `TimeoutException`, to be tried again. A
  request already sent runs to its answer, whatever the caller's cancellation, since the provider rotates the refresh
  token as it answers, and its result is written under the grant's fence. So a store whose writes take most of a lease
  fails requests to be retried, and never starts a refresh it cannot record.
- **The result write must land.** It keeps one version across its attempts, which start a third of an interval apart
  within a window of one lease while renewals go on, each running to the window's end. A refused attempt means the row
  holds another grant, possibly this very result landed by an earlier attempt: the row is read, and the call succeeds
  if it holds the result and otherwise reads the row again. When no attempt is known to have landed by the window's
  end, the claim is released, renewals going on beside it, and the row is read once every attempt has answered, or an
  interval on:
  - a row holding this result means it was stored: the call succeeds, with no error;
  - a row holding another grant means the call reads it again and goes on as any other;
  - while an attempt at the result is still pending in the store, or when the row cannot be read, whether it was
    stored is not known: the row id is logged at Warning and the call fails with `TimeoutException`, to be tried again;
  - a row still holding the grant refreshed from, with every attempt answered, means the result was not stored: the
    row id is logged at Error, never a token, the request fails, and the next use finds the old refresh token rejected
    and the person signs in again.

Every other request for that DID, in any process, waits — pausing 50 ms, doubling to 1 s — for the stored grant or for
the claim to lapse, and claims it then. Two numbers bound this, and they differ on purpose:

- **One request's wait** is one request timeout, 30 s with the defaults. A request that has not claimed the row by then
  fails with `TimeoutException` — another request is refreshing this sign-in, try again — logged at Information with
  the row id. It has written nothing, so the grant is untouched. A request that claimed the row keeps the claim's own
  bounds.
- **The takeover horizon** is the longest a claim stands while its holder works: two provider requests, a refresh or a
  revocation and its nonce retry, each within the request timeout; an interval waiting for room; and three leases — the
  result's window, the release, and the lease last renewed before the holder settles. With the defaults that is
  2 × 30 s + 5 s + 3 × 15 s: 110 s. A waiter takes over only once the lease lapses, and a holder renews it until it
  settles, so no waiter takes over from a working holder; a holder that stopped is taken over once its lease lapses.

A web request is never held for the horizon: a refresh typically settles in well under a second, and a waiter's request
that outlasts one request timeout gives up to be tried again, while the waiter never takes over from a working holder.
So two processes do not present one refresh token while the holder's renewals land, and the newer tokens stand. A
provider that answers `invalid_grant` has ended the grant, and the row ends with it; any other failure releases the
claim and keeps the session.

`Disconnect` builds the client and reads the issuer's revocation endpoint first, then claims the grant, so the claim
covers the revocation request and its nonce retry alone, and a claim without room for them is released and the row
read again, up to the wait bound. It ends the row, under the claim's fence, only once revocation is confirmed. A
disconnect whose end is refused — the person signed in again meanwhile, another process took the claim, or a late
result cleared it — throws, saying the session was not ended; it never reports success over a row it did not end.

**What the fences cannot close.**

- A holder that lands no renewal before its lease ends — paused, or cut off from the store for longer than the lease —
  while its request is at the provider can be taken over. Its late result still lands while the row holds the grant it
  refreshed from, and clears the taker's claim even after the taker began: a taker that had not yet sent its own
  refresh finds its claim gone when it begins, and refreshes nothing; one that already sent it presented the same
  refresh token, and a provider that ends replayed grants then ends the grant. A taker that was revoking has its end
  refused: `Disconnect` says the session changed, and a retry revokes the new token. Only fencing at the provider
  closes this, and the AT reference provider has none.
- A store whose writes take more than half a lease serves no refresh until it speeds up: every request whose access
  token is due fails, to be tried again. The grant is never ended by it.
- A request whose answer is lost after the provider rotated the token — a connection error after sending — leaves the
  stored refresh token rotated away. No client can tell that case apart; the next refresh is refused, the row ends, and
  the person signs in again.
- Clocks: a waiter takes over when its own clock passes the `ClaimedUntil` the holder wrote with its clock. While the
  holder's renewals land, a waiter whose clock runs ahead of the holder's is tolerated by up to the lease's lifetime less
  one renewal interval and a renewal's write latency — under 10 seconds with the defaults. Nothing is tolerated once
  renewals stop.

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
- `SessionClaims.A_waiter_takes_over_within_the_lease_lifetime_of_a_holder_that_stopped`: with the holder's lease at one
  second, the waiter refreshes within three, and the stopped holder's end and refreshed write are refused. Measured
  before on the claim of four request timeouts: a waiter bounded at 20 s was still waiting.
- `ClaimInterleavings`, the interleavings a claim must survive, over SQLite, with leases of two seconds renewed every
  half second. Each is named with how an earlier revision fared: the claim without a claim identity where no revision
  is named, and otherwise the revision named.
  - `Renewals_that_fail_while_a_refresh_is_at_the_provider_and_then_recover_still_store_that_refresh`: renewals fail
    past half the lease and recover while the refresh is at the provider; the send answers 200 after one refresh and
    the stored token is the provider's. Before: the lost claim cancelled the refresh, the same call claimed again and
    replayed the old token, and the provider ended the grant.
  - `A_renewal_that_lands_unrecognized_past_its_bound_never_costs_the_result`, with the read-back fast and slow: the
    result is stored. Before: with a slow read-back the holder's own result was refused.
  - `A_holder_that_cannot_renew_starts_no_new_request_and_with_no_takeover_its_request_in_flight_lands`: no renewal
    lands at all, and the refresh already at the provider is stored after the lease ended, the row still carrying the
    claim. Before: the send failed.
  - `A_store_cut_shorter_than_the_lease_with_a_second_process_sending_costs_one_refresh_and_no_grant`: both sends answer
    200 after one refresh, and the grant lives. Before: the replay ended the grant.
  - `A_renewal_after_a_result_that_landed_unrecognized_never_restores_the_old_refresh_token`: the rotated token stays.
    This one held before too.
  - `A_result_write_that_stalls_within_its_bound_never_stops_renewals_and_the_waiter_waits_and_reads_it`: the provider
    answers at 1.5 s and the store stalls the result until 3 s, past the claim's first lease and within the result's
    window; the waiter, with a request timeout of 2 s, finds the row still claimed, waits, and reads the stored grant.
    It passed on `e39b69651` too.
  - `A_result_write_stalled_past_its_bound_is_given_up_within_it_and_the_waiter_goes_on`: a holder whose result stalls
    nine seconds gives it up at the end of its window, releases, and the waiter, with a request timeout of 3 s, claims
    and refreshes itself. Before
    (`e39b69651`): the result write was unbounded, and the waiter gave up with a timeout while the holder still worked.
  - `A_result_that_landed_before_the_store_went_down_is_stored_and_the_call_answers`: the result lands, its answer fails
    and the store is out for six seconds within a nine-second lease; the call answers 200 with the stored token. Before
    (`e39b69651`): the call failed with the result stored.
  - `A_result_whose_release_is_refused_because_it_landed_is_reported_stored_without_an_error`: no read answers until the
    release, which is refused because the result landed; the result is reported stored, with no error logged. Before
    (`e39b69651`): it threw and logged an error.
  - `A_renewal_hung_through_an_outage_of_little_more_than_an_interval_is_renewed_once_the_store_is_back`, at production's
    ratio of a lease to three intervals, with reads that hang and reads that fail: the store is out from the claim until
    80 ms past two intervals, and back 420 ms before the lease ends; the lease is renewed under the holder's claim, the
    waiter waits, and the grant lives. Before (`e39b69651`): with hanging reads one renewal used up the rest of the
    lease, the waiter took over, and the grant ended; with failing reads it held.
  - `A_store_whose_every_write_is_slow_answers_or_fails_to_be_retried_and_never_ends_the_grant`, every session write
    taking 120, 400, 700, 900, 1,200 and 1,600 ms with a request timeout of two seconds: up to 900 ms both sends answer
    200; at 1,200 and 1,600 ms both fail with `TimeoutException` and no refresh starts; at every latency the grant lives
    and the stored refresh token is the provider's. Before (`9951f9349`, every write cut off at a sixth of an
    interval): from 120 to 1,200 ms the first send failed "could not be stored", the second required authorization
    again, and the provider had ended the grant; at 1,600 ms both timed out.
  - `A_claim_starts_no_request_with_less_than_half_its_lease_left_whether_renewals_fail_fast_or_stall`: with the whole
    lease left a request starts; with 950 ms of 2 s left none does, after waiting an interval for a renewal. Before
    (`9951f9349`): a request started with 950 ms left.
  - `A_claim_that_lands_with_less_than_half_its_lease_left_renews_before_its_request_starts`: the claim's write takes
    1.1 s; the refresh starts once a renewal lands, and is stored. Before (`9951f9349`): it started with 0.9 s of the
    lease left, before any renewal.
  - `Renewals_that_land_out_of_order_only_extend_the_lease_and_the_claim_holds`, over SQLite and DuckDB: the first
    renewal takes 600 ms and the next lands first; the first reaches the store and is refused by the longer lease, and
    both sends answer 200 after one refresh. Before (`9951f9349`): the slow renewal was cut off before it reached the
    store.
  - `A_waiter_that_takes_over_a_lapsed_claim_without_room_leaves_the_holders_late_result_standing`: once the holder
    began, every write takes 1.6 s, so its lease lapses while the provider takes 2.5 s; the waiter, reading just after,
    takes the claim over and finds no room. The takeover lands before the holder's result, which clears it; both sends
    answer 200 after one refresh, the grant lives, and a later send answers 200. Before (`cfb7275e6`): the result,
    fenced on the holder's claim, was refused, and the holder's send failed.
  - `A_late_result_over_a_takeover_stands_and_the_takers_begin_and_release_are_refused`: a holder whose lease lapsed
    stores its result after another process took the claim over; the taker's begin reads the row and starts nothing,
    its release is refused, and the rotated token stands with no claim. Before (`cfb7275e6`): the result was refused.
  - `A_stale_result_after_a_new_sign_in_is_refused_and_the_sign_in_stands`. It held on `cfb7275e6` too.
  - `Two_holders_refreshing_from_one_grant_store_one_result`, over SQLite and DuckDB: the taker's result lands, and the
    lapsed holder's, from the same grant, is refused. It held on `cfb7275e6` too, over SQLite.
  - `A_result_that_landed_with_its_answer_lost_is_reported_stored_and_no_renewal_undoes_it` and
    `A_second_process_reading_while_a_result_stalls_within_its_window_finds_the_row_claimed_and_the_result_lands`, the
    reviewer's N1 and N5 under the grant's fence. Both held on `cfb7275e6` too.
  - `A_waiter_behind_a_holder_working_past_one_request_timeout_fails_to_be_retried_then_and_the_holders_result_lands`:
    the holder's refresh takes three seconds at the provider; the waiter, with a request timeout of 1 s, fails with
    `TimeoutException` within half a second of it, logged at Information with no warning or error, and the holder's
    result lands with the provider's token. Before (`40905abd7`): the waiter waited out the holder and answered 200.
  - `A_result_still_pending_in_a_store_that_ignores_cancellation_is_reported_unknown_and_lands_later`: the result and
    the release each take five seconds and the store makes them whatever the caller's cancellation; the call fails with
    `TimeoutException`, the row id logged at Warning and no error, and the result lands afterwards with the claim
    cleared. Before (`9951f9349`): the call reported the result not stored, logged an error, and the result then landed.
- `SessionClaims.A_store_that_fails_once_on_the_refreshed_write_still_keeps_the_rotated_tokens`, for a write that fails
  before it lands and one that fails after: the send answers 200 and the stored refresh token is the provider's. Measured
  before: the send failed, the stored refresh token was one the provider had rotated away, and the claim stayed.
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
process, since Koan's Entity statics are per process; process clocks that differ; and a public provider. The residuals
§3 names are not closed by anything here.
