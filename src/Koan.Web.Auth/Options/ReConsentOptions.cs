namespace Koan.Web.Auth.Options;

public sealed class ReConsentOptions
{
    /// <summary>
    /// Binding a login needs a sign-in no older than <see cref="MaxAge"/>: a person who signed in longer ago signs in again
    /// first, so a cookie alone binds nothing. On unless an application turns it off.
    /// </summary>
    public bool ForceOnLink { get; init; } = true;

    /// <summary>How recent a sign-in <see cref="ForceOnLink"/> accepts.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(10);
}
