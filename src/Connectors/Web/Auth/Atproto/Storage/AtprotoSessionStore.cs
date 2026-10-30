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
/// read, or, for a refresh's result, on the grant it refreshed from. A row that cannot be unprotected reads as no session
/// for its own DID and nothing else.
/// </summary>
internal sealed class AtprotoSessionStore(IDataProtectionProvider protection, ILogger<AtprotoSessionStore>? logger = null) : IOAuthStateStore
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
    /// Claims <paramref name="read"/>'s grant for this process to refresh or revoke it, for <paramref name="lease"/>. Null
    /// when the row changed since it was read, another claim included.
    /// </summary>
    public async Task<SessionClaim?> Claim(StoredSession read, TimeSpan lease, CancellationToken ct)
    {
        var row = Copy(read.Row);
        row.ClaimedUntil = DateTimeOffset.UtcNow + lease;
        return await Replace(row, read.Row.Version, ct) ? new SessionClaim(this, row) : null;
    }

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

    // ---- Writes that settle a claim ----

    /// <summary>
    /// A refreshed grant: <paramref name="next"/>, held by no claim, in place of the row only while the row still holds
    /// <paramref name="from"/>, the protected grant the refresh presented, whichever claim holds it then.
    /// </summary>
    internal Task<bool> WriteRefreshed(AtprotoSession next, string from)
        => Settling(ct => AtprotoSession.ReplaceIf(next, stored => stored.Grant == from, ct: ct));

    /// <summary>The grant as it was, held by no claim, only while the row is as the claim wrote it.</summary>
    internal Task<bool> Release(AtprotoSession claimed) => Settling(ct => Replace(Copy(claimed), claimed.Version, ct));

    /// <summary>
    /// Ends the claimed session, only while the row is as the claim wrote it: deleted where the store deletes conditionally,
    /// and otherwise emptied in place to its DID, expiring now so the next sweep removes it.
    /// </summary>
    internal Task<bool> End(AtprotoSession claimed)
    {
        var version = claimed.Version;
        if (Data<AtprotoSession, string>.Capabilities.Has(DataCaps.Write.ConditionalDelete))
            return Settling(ct => Data<AtprotoSession, string>.DeleteIf(claimed.Id, stored => stored.Version == version, ct: ct));
        return Settling(ct => Replace(new AtprotoSession { Id = claimed.Id, Did = claimed.Did, Version = NewVersion(), ExpiresAt = DateTimeOffset.UtcNow }, version, ct));
    }

    // What settles a claim no longer answers to its caller's cancellation, since the provider has already acted; it has a
    // bound of its own, which the claim's lease covers.
    private static async Task<bool> Settling(Func<CancellationToken, Task<bool>> write)
    {
        using var bound = new CancellationTokenSource(Constants.ClaimWriteBound);
        return await write(bound.Token);
    }

    internal AtprotoSession Granted(string id, string did, OAuthSessionData grant) => new()
    {
        Id = id, Did = did, Version = NewVersion(),
        Grant = sessions.Protect(JsonSerializer.Serialize(grant)),
        ExpiresAt = DateTimeOffset.UtcNow + Constants.RefreshTokenLifetime,
    };

    // The row's grant under a new version, held by no claim.
    private static AtprotoSession Copy(AtprotoSession row) => new()
    {
        Id = row.Id, Did = row.Did, Version = NewVersion(), Grant = row.Grant, ExpiresAt = row.ExpiresAt,
    };

    internal void LogUnstored(string row, Exception failure)
        => log.LogWarning(failure, "The refreshed AT session of row {Row} could not be stored, and the request fails, to be tried again.", row);

    private static Task<bool> Replace(AtprotoSession row, string version, CancellationToken ct)
        => AtprotoSession.ReplaceIf(row, stored => stored.Version == version, ct: ct);

    private static string NewVersion() => Guid.NewGuid().ToString("N");
}
