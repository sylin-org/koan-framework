using System.ComponentModel;

namespace Koan.Web.Auth.Providers;

/// <summary>Read-only, credential-free projection of one host's compiled authentication provider decisions.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IAuthProviderCatalog
{
    IReadOnlyList<AuthProviderInfo> Providers { get; }
    AuthProviderInfo? Default { get; }
    AuthProviderInfo? Find(string? id);
}

/// <summary>Credential-free provider availability and election evidence shared with optional auth modules.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record AuthProviderInfo(
    string Id,
    string DisplayName,
    string Protocol,
    string State,
    bool Eligible,
    bool Explicit,
    bool Automatic,
    int Priority,
    string Reason,
    string? Correction,
    string? Icon,
    string[] Scopes,
    string? ChallengePath);
