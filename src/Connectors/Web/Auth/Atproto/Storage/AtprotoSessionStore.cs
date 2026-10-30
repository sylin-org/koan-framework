using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet.OAuth.Storage;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// AT sessions and pending authorizations as rows in the application's own store, each protected on its own with the
/// Data Protection key ring. Every read is one keyed read or a bounded page, and nothing is held in the process: two
/// processes over one store sign people in alike. A session row changes only by a conditional write: on the version it
/// read, or, for a claim holder's own writes, on its claim identity. A row that cannot be unprotected reads as no session
/// for its own DID and nothing else.
/// </summary>
internal sealed class AtprotoSessionStore(IDataProtectionProvider protection, ILogger<AtprotoSessionStore>? logger = null,
    TimeSpan? claimLifetime = null, TimeSpan? claimRenewal = null) : IOAuthStateStore
{
    private readonly IDataProtector sessions = protection.CreateProtector(Constants.SessionProtectionPurpose);
    private readonly IDataProtector states = protection.CreateProtector(Constants.StateProtectionPurpose);
    private readonly ILogger log = logger ?? NullLogger<AtprotoSessionStore>.Instance;

    // ---- Pending authorizations ----

    /// <summary>Keeps a pending authorization until <see cref="OAuthStateData.ExpiresAt"/>.</summary>
    public Task StoreAsync(string state, OAuthStateData value, CancellationToken cancellationToken = default)
        => new AtprotoAuthorizationState
        {
            Id = AtprotoAuthorizationState.KeyFor(state),
            Pending = states.Protect(JsonSerializer.Serialize(value)),
            ExpiresAt = value.ExpiresAt,
        }.Save(cancellationToken);

    /// <summary>The pending authorization pushed with <paramref name="state"/> while it is live, without consuming it.</summary>
    public async Task<OAuthStateData?> Peek(string state, CancellationToken ct)
        => Key(state) is { } id && await AtprotoAuthorizationState.Get(id, ct) is { } row ? OpenLive(row) : null;

    /// <summary>
    /// Consumes the pending authorization pushed with <paramref name="state"/>: of any number of concurrent consumers,
    /// the one whose delete removes the row receives it, and only while it is live.
    /// </summary>
    public async Task<OAuthStateData?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        if (Key(state) is not { } id || await AtprotoAuthorizationState.Get(id, cancellationToken) is not { } row) return null;
        return await AtprotoAuthorizationState.Remove(id, cancellationToken) ? OpenLive(row) : null;
    }

    private OAuthStateData? OpenLive(AtprotoAuthorizationState row)
    {
        if (row.ExpiresAt <= DateTimeOffset.UtcNow) return null;
        try { return JsonSerializer.Deserialize<OAuthStateData>(states.Unprotect(row.Pending)); }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            log.LogWarning("A pending AT authorization could not be read with this application's key ring; its sign-in starts again.");
            return null;
        }
    }

    // A state the provider echoes is a query value; one that cannot name a row names no authorization.
    private static string? Key(string state)
    {
        try { return AtprotoAuthorizationState.KeyFor(state); }
        catch (ArgumentException) { return null; }
    }

    // ---- Sessions ----

    /// <summary>
    /// <paramref name="did"/>'s session row — one keyed read — and its grant when the row holds a live one this
    /// application can read; null when there is no row. A grant that cannot be unprotected, or that names another
    /// subject, is logged and read as none: the person signs in again, and the new grant replaces the row.
    /// </summary>
    public async Task<StoredSession?> Read(string did, CancellationToken ct)
    {
        var row = await AtprotoSession.Get(AtprotoSession.KeyFor(did), ct);
        return row is null || !string.Equals(row.Did, did, StringComparison.Ordinal) ? null : new StoredSession(row, Open(row));
    }

    private OAuthSessionData? Open(AtprotoSession row)
    {
        if (row.Grant is null || row.ExpiresAt is not { } expires || expires <= DateTimeOffset.UtcNow) return null;
        try
        {
            var grant = JsonSerializer.Deserialize<OAuthSessionData>(sessions.Unprotect(row.Grant));
            if (grant is not null && string.Equals(grant.TokenSet.Sub, row.Did, StringComparison.Ordinal)) return grant;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            log.LogWarning(error, "The AT session of {Did} could not be read with this application's key ring; the person signs in again.", row.Did);
            return null;
        }

        log.LogWarning("The AT session row of {Did} holds a grant for another subject; the person signs in again.", row.Did);
        return null;
    }

    /// <summary>
    /// Makes <paramref name="grant"/> <paramref name="did"/>'s session, replacing <paramref name="read"/> only if it is
    /// still what is stored. Null when the row changed since it was read; the caller reads again and decides again.
    /// </summary>
    public async Task<StoredSession?> Commit(string did, OAuthSessionData grant, StoredSession? read, CancellationToken ct)
    {
        var row = Granted(AtprotoSession.KeyFor(did), did, grant);
        if (read is not null) return await Replace(row, read.Row.Version, ct) ? new StoredSession(row, grant) : null;
        if (!Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.InsertOnly))
        {
            // A store that cannot insert only when absent: of two first sign-ins of one DID at once, the later stands.
            await row.Save(ct);
            return new StoredSession(row, grant);
        }

        return (await AtprotoSession.Insert(row, ct: ct)).Outcome == MutationOutcome.Inserted ? new StoredSession(row, grant) : null;
    }

    /// <summary>
    /// Claims <paramref name="read"/>'s grant for this process to refresh or revoke it, under a claim identity of its own:
    /// a lease of <see cref="ClaimLifetime"/> that the claim renews while its holder works. Null when the row changed
    /// since it was read, another claim included.
    /// </summary>
    public async Task<SessionClaim?> Claim(StoredSession read, CancellationToken ct)
    {
        var row = Copy(read.Row);
        row.ClaimId = NewVersion();
        row.ClaimedUntil = DateTimeOffset.UtcNow + ClaimLifetime;
        return await Replace(row, read.Row.Version, ct) ? new SessionClaim(this, read with { Row = row }) : null;
    }

    /// <summary>How long a claim stands unless its holder renews it: the longest a stopped holder keeps others waiting.</summary>
    public TimeSpan ClaimLifetime { get; } = claimLifetime ?? Constants.ClaimLifetime;

    /// <summary>How long after the lease last landed started its holder renews it.</summary>
    public TimeSpan ClaimRenewal { get; } = claimRenewal ?? Constants.ClaimRenewal;

    /// <summary>
    /// How far apart attempts at a write under a claim start while earlier ones have not answered: a third of a renewal
    /// interval. No attempt is cut off on it; each runs until the store answers or its deadline passes.
    /// </summary>
    public TimeSpan AttemptSpacing => ClaimRenewal / Constants.ClaimAttemptsPerInterval;

    /// <summary>The least lease a provider request starts under: half a lease, so the store records what it returns.</summary>
    public TimeSpan RequestRoom => ClaimLifetime / 2;

    /// <summary>
    /// The longest a claim stands beyond its holder's provider requests: an interval waiting for a renewal to give a request
    /// room, a lease for the result's attempts, at most a lease for the release, and the lease last renewed before the
    /// holder settles. With the defaults, 5 s + 3 × 15 s: 50 s. With the requests, it is the horizon within which no waiter
    /// takes over from a working holder, since the holder renews its lease until it settles.
    /// </summary>
    public TimeSpan ClaimHold => ClaimRenewal + ClaimLifetime * 3;

    /// <summary>
    /// Removes the pending authorizations and the session rows that expired by <paramref name="now"/> — sessions not
    /// refreshed in time, and sessions that ended — reading <paramref name="pageSize"/> rows at a time. Where the store
    /// deletes conditionally a row is deleted only if it is still the version read; elsewhere it is read again by its key
    /// just before an unconditional delete. A page that removes nothing ends its pass, so a sweep always finishes.
    /// </summary>
    /// <returns>How many rows it removed.</returns>
    public async Task<int> Sweep(DateTimeOffset now, int pageSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        var page = new QueryDefinition().WithPagination(1, pageSize);
        var removed = 0;
        while (true)
        {
            var expired = await AtprotoAuthorizationState.Query(row => row.ExpiresAt <= now, page, ct);
            var count = 0;
            foreach (var row in expired)
                if (await AtprotoAuthorizationState.Remove(row.Id, ct)) count++;
            removed += count;
            if (expired.Count < pageSize || count == 0) break;
        }

        var conditional = Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.ConditionalDelete);
        while (true)
        {
            var expired = await AtprotoSession.Query(row => row.ExpiresAt != null && row.ExpiresAt <= now, page, ct);
            var count = 0;
            foreach (var row in expired)
                if (await Prune(row, conditional, ct)) count++;
            removed += count;
            if (expired.Count < pageSize || count == 0) break;
        }

        return removed;
    }

    // A store without conditional deletion leaves one window: a sign-in that replaces the row between the read again and
    // the delete is deleted with it, and the person signs in again (WEB-0082).
    private static async Task<bool> Prune(AtprotoSession row, bool conditional, CancellationToken ct)
    {
        var version = row.Version;
        if (conditional) return await Data<AtprotoSession, string>.DeleteIf(row.Id, stored => stored.Version == version, ct: ct);
        return await AtprotoSession.Get(row.Id, ct) is { } current && current.Version == version
            && await AtprotoSession.Remove(row.Id, ct);
    }

    // ---- Writes under a claim ----

    /// <summary>
    /// <paramref name="next"/> in place of the row only while claim <paramref name="claimId"/> holds it: one attempt, until
    /// the store answers or <paramref name="deadline"/> passes.
    /// </summary>
    internal Task<WriteOutcome> Write(AtprotoSession next, string claimId, DateTimeOffset deadline)
        => Attempt(ct => AtprotoSession.ReplaceIf(next, stored => stored.ClaimId == claimId, ct: ct), deadline, CancellationToken.None);

    /// <summary>
    /// A refreshed grant: <paramref name="next"/>, held by no claim, in place of the row only while the row still holds
    /// <paramref name="from"/>, the protected grant the refresh presented, whichever claim holds it then. One attempt, until
    /// the store answers or <paramref name="deadline"/> passes. <paramref name="pending"/> receives the store call, which a
    /// store that ignores cancellation may still complete after the attempt gave it up.
    /// </summary>
    internal Task<WriteOutcome> WriteRefreshed(AtprotoSession next, string from, DateTimeOffset deadline, Action<Task> pending)
        => Attempt(ct => AtprotoSession.ReplaceIf(next, stored => stored.Grant == from, ct: ct), deadline, CancellationToken.None, pending);

    /// <summary>
    /// A renewal: <paramref name="next"/> in place of the row only while claim <paramref name="claimId"/> holds it under a
    /// shorter lease, so a renewal only ever extends the lease, whatever order renewals land in. One attempt, until the
    /// store answers, <paramref name="deadline"/> passes or <paramref name="stop"/> is cancelled.
    /// </summary>
    internal Task<WriteOutcome> Renew(AtprotoSession next, string claimId, DateTimeOffset deadline, CancellationToken stop)
    {
        var until = next.ClaimedUntil!.Value;
        return Attempt(ct => AtprotoSession.ReplaceIf(next, stored => stored.ClaimId == claimId && stored.ClaimedUntil < until, ct: ct),
            deadline, stop);
    }

    /// <summary>
    /// Ends the session claim <paramref name="claimId"/> holds: deleted where the store deletes conditionally, and
    /// otherwise emptied in place to its DID, expiring now so the next sweep removes it. One attempt, as <see cref="Write"/>.
    /// </summary>
    internal Task<WriteOutcome> End(AtprotoSession row, string claimId, DateTimeOffset deadline)
    {
        if (!Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.ConditionalDelete))
            return Write(new AtprotoSession { Id = row.Id, Did = row.Did, Version = NewVersion(), ExpiresAt = DateTimeOffset.UtcNow }, claimId, deadline);
        return Attempt(ct => Data<AtprotoSession, string>.DeleteIf(row.Id, stored => stored.ClaimId == claimId, ct: ct),
            deadline, CancellationToken.None);
    }

    /// <summary>The row as stored now, read once before <paramref name="deadline"/>; not answered when it cannot be.</summary>
    internal async Task<(bool Answered, AtprotoSession? Row)> Peek(string id, DateTimeOffset deadline, CancellationToken stop = default)
    {
        var bound = deadline - DateTimeOffset.UtcNow;
        if (bound <= TimeSpan.Zero || stop.IsCancellationRequested) return (false, null);
        var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(bound);
        Task<AtprotoSession?> read;
        try { read = AtprotoSession.Get(id, limit.Token); }
        catch (Exception) { limit.Dispose(); return (false, null); }
        try { return (true, await read.WaitAsync(limit.Token)); }
        catch (Exception) { return (false, null); }
        finally { Leave(read, limit); }
    }

    // One attempt at a write under a claim: it runs until the store answers or the deadline passes, and is cancelled then,
    // or when stop is. A store that ignores cancellation is left behind at the deadline; the fence settles whatever it lands.
    private static async Task<WriteOutcome> Attempt(Func<CancellationToken, Task<bool>> call, DateTimeOffset deadline,
        CancellationToken stop, Action<Task>? pending = null)
    {
        var bound = deadline - DateTimeOffset.UtcNow;
        if (bound <= TimeSpan.Zero || stop.IsCancellationRequested) return WriteOutcome.Unknown(null);
        var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
        limit.CancelAfter(bound);
        Task<bool> write;
        try { write = call(limit.Token); }
        catch (Exception error) { limit.Dispose(); return WriteOutcome.Unknown(error); }
        pending?.Invoke(write);
        try { return await write.WaitAsync(limit.Token) ? WriteOutcome.Landed : WriteOutcome.Refused; }
        catch (Exception error) { return WriteOutcome.Unknown(error); }
        finally { Leave(write, limit); }
    }

    // A store call given up while still running keeps its token until it ends, and its late failure is observed.
    private static void Leave(Task call, CancellationTokenSource limit)
    {
        if (call.IsCompleted)
        {
            _ = call.Exception;
            limit.Dispose();
            return;
        }

        _ = call.ContinueWith(static (task, state) => { _ = task.Exception; ((CancellationTokenSource)state!).Dispose(); }, limit,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The row with the claim's lease extended to <paramref name="until"/>, still under claim <paramref name="claimId"/>.</summary>
    internal static AtprotoSession Renewed(AtprotoSession row, string claimId, DateTimeOffset until)
    {
        var next = Copy(row);
        next.ClaimId = claimId;
        next.ClaimedUntil = until;
        return next;
    }

    internal AtprotoSession Granted(string id, string did, OAuthSessionData grant) => new()
    {
        Id = id, Did = did, Version = NewVersion(),
        Grant = sessions.Protect(JsonSerializer.Serialize(grant)),
        ExpiresAt = DateTimeOffset.UtcNow + Constants.RefreshTokenLifetime,
    };

    /// <summary>The row's grant under a new version, held by no claim: every write that keeps a claim says so itself.</summary>
    internal static AtprotoSession Copy(AtprotoSession row) => new()
    {
        Id = row.Id, Did = row.Did, Version = NewVersion(), Grant = row.Grant, ExpiresAt = row.ExpiresAt,
    };

    internal void LogUnstored(AtprotoSession row, Exception? failure)
        => log.LogError(failure, "The refreshed AT session of row {Row} was not stored, and no attempt at it is pending; the person " +
            "signs in again at its next use.", row.Id);

    internal void LogWaited(string row, TimeSpan waited, TimeSpan horizon)
        => log.LogInformation("A request waited {Waited} for another request refreshing the AT session of row {Row}, and gives " +
            "up to be tried again; that claim stands at most {Horizon} while its holder works.", waited, row, horizon);

    internal void LogTooSlow(string row, TimeSpan waited)
        => log.LogWarning("For {Waited}, every claim on the AT session of row {Row} landed with less than half a lease left, too " +
            "late to record a refresh; the request gives up to be tried again, and the grant is untouched.", waited, row);

    internal void LogUnknown(AtprotoSession row, Exception? failure)
        => log.LogWarning(failure, "Whether the refreshed AT session of row {Row} was stored is not known: an attempt at it is still " +
            "pending in the store, or the store could not be read. The request fails, to be tried again.", row.Id);

    private static Task<bool> Replace(AtprotoSession row, string version, CancellationToken ct)
        => AtprotoSession.ReplaceIf(row, stored => stored.Version == version, ct: ct);

    private static string NewVersion() => Guid.NewGuid().ToString("N");
}
