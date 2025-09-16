using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Roles.Contracts;
using Koan.Web.Auth.Roles.Options;

namespace Koan.Web.Auth.Roles.Services;

public sealed class DefaultRoleConfigSnapshotProvider : IRoleConfigSnapshotProvider
{
    private readonly IRoleAliasStore _aliases;
    private readonly IRolePolicyBindingStore _bindings;
    private readonly IOptionsMonitor<RoleAttributionOptions> _options;
    private volatile RoleConfigSnapshot _snapshot;

    public DefaultRoleConfigSnapshotProvider(IRoleAliasStore aliases, IRolePolicyBindingStore bindings, IOptionsMonitor<RoleAttributionOptions> options)
    {
        _aliases = aliases;
        _bindings = bindings;
        _options = options;
        _snapshot = new RoleConfigSnapshot
        {
            Aliases = options.CurrentValue.Aliases.Map.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            PolicyBindings = options.CurrentValue.PolicyBindings.ToDictionary(x => x.Id, x => x.Requirement, StringComparer.OrdinalIgnoreCase),
            LoadedAt = DateTimeOffset.UtcNow
        };
    }

    public RoleConfigSnapshot Get() => _snapshot;

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        var aliases = await _aliases.All(ct);
        var bindings = await _bindings.All(ct);
        _snapshot = new RoleConfigSnapshot
        {
            Aliases = aliases.ToDictionary(a => a.Id, a => a.TargetRole, StringComparer.OrdinalIgnoreCase),
            PolicyBindings = bindings.ToDictionary(b => b.Id, b => b.Requirement, StringComparer.OrdinalIgnoreCase),
            LoadedAt = DateTimeOffset.UtcNow
        };
    }
}
