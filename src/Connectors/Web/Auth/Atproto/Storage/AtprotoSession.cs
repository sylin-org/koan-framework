using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// One DID's AT session: its grant — tokens, DPoP key and the client identity it was granted to — protected on its own
/// with the application's Data Protection key ring. The row is read by its key, <see cref="KeyFor"/>, and every write
/// is conditional — on the <see cref="Version"/> it read; for a refresh's result, on the <see cref="Grant"/> it refreshed
/// from — so a write made from a state another process has since changed is refused rather than applied.
/// <see cref="AtprotoSessionStore"/> is its only owner.
/// </summary>
internal sealed class AtprotoSession : Entity<AtprotoSession>, IAmbientExempt
{
    /// <summary>The DID, exactly; a row read back for another DID is not this one's.</summary>
    public string Did { get; set; } = "";

    /// <summary>Changed by every write to a value no write used before, so a guard on it never matches a newer state.</summary>
    public string Version { get; set; } = "";

    /// <summary>The protected grant, or null once the session ended on a store that cannot delete conditionally.</summary>
    public string? Grant { get; set; }

    /// <summary>When the row is pruned: once its grant can no longer be refreshed, or at once when its session ended on a
    /// store that cannot delete conditionally.</summary>
    [Index]
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Until when one process holds the grant to refresh or revoke it; others wait for it or for this time. Null
    /// when no claim holds the row.</summary>
    public DateTimeOffset? ClaimedUntil { get; set; }

    /// <summary>The row id of <paramref name="did"/>'s session.</summary>
    public static string KeyFor(string did) => StorageKeys.Compose(nameof(AtprotoSession), did);
}
