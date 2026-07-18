using System.Text.Json.Serialization;

namespace Koan.Web.Auth.Domain;

/// <summary>
/// Response payload for <c>GET /me</c>. Lean identity fields are always present; <see cref="Email"/>,
/// <see cref="Roles"/>, and <see cref="Claims"/> are populated by the default projector when the
/// authentication cookie carries them, and omitted from the serialized response when null/empty so
/// older clients that expect only the lean shape continue to round-trip unchanged.
/// </summary>
public sealed class CurrentUserDto
{
    /// <summary>Platform user id (cookie <c>sub</c> / <c>NameIdentifier</c>).</summary>
    public string? Id { get; init; }

    /// <summary>Display name from the cookie's <c>name</c> claim or the user store fallback.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Email claim, if present on the principal. Surfaced so SPAs can render account chrome
    /// (initials, avatar fallback) without a second lookup.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; init; }

    /// <summary>Avatar / picture URL from the cookie's <c>picture</c> claim.</summary>
    public string? PictureUrl { get; init; }

    /// <summary>Platform roles. Surfaced so SPAs can render role-gated UI on first paint without
    /// probing protected endpoints. Empty when the cookie carries no role claims.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Every cookie claim grouped by type. Lets SPAs read custom permissions, tenant ids,
    /// or any other provider-issued claim without a per-feature projection. Omitted when empty.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Linked external identities (provider + key-hash + display name).</summary>
    public IReadOnlyList<ConnectionDto> Connections { get; init; } = [];
}
