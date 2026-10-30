using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Connector.Atproto.Storage;

/// <summary>
/// One pending authorization: the PKCE verifier, DPoP key and binding a sign-in carries from its challenge to its
/// callback, protected on its own. Its id is composed from the state the provider echoes (<see cref="KeyFor"/>), so the
/// state itself is never stored. It is read only while live, consumed once — the one consumer whose delete removes the
/// row receives it — and swept a page at a time once expired.
/// </summary>
internal sealed class AtprotoAuthorizationState : Entity<AtprotoAuthorizationState>, IAmbientExempt
{
    /// <summary>The protected pending authorization.</summary>
    public string Pending { get; set; } = "";

    /// <summary>When the authorization stops being accepted.</summary>
    [Index]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The row id of the authorization pushed with <paramref name="state"/>.</summary>
    public static string KeyFor(string state) => StorageKeys.Compose(nameof(AtprotoAuthorizationState), state);
}
