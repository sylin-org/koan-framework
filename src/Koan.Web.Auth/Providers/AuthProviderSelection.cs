namespace Koan.Web.Auth.Providers;

public sealed record AuthProviderSelection(
    string? ProviderId,
    string Protocol,
    string Reason,
    int Priority,
    bool IsFallback,
    string? ChallengePath)
{
    public bool HasProvider => !string.IsNullOrWhiteSpace(ProviderId);
    public bool SupportsInteractiveChallenge => !string.IsNullOrWhiteSpace(ChallengePath);

    public static AuthProviderSelection None { get; } = new(null, "none", "No providers were elected.", 0, false, null);
}
