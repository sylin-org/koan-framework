using System.Collections.Immutable;

namespace Sora.Web.Auth.Roles.Contracts;

public interface IRoleConfigSnapshotProvider
{
    RoleConfigSnapshot Get();
    Task ReloadAsync(CancellationToken ct = default);
}

public sealed class RoleConfigSnapshot
{
    public required IReadOnlyDictionary<string, string> Aliases { get; init; }
    public required IReadOnlyDictionary<string, string> PolicyBindings { get; init; }
    public DateTimeOffset LoadedAt { get; init; }
}
