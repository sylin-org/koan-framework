using Microsoft.Extensions.Logging;
using Sora.Web.Auth.Roles.Contracts;
using Sora.Web.Auth.Roles.Model;

namespace Sora.Web.Auth.Roles.Services.Stores;

public sealed class DefaultRoleStore : IRoleStore
{
    private readonly ILogger<DefaultRoleStore> _logger;
    public DefaultRoleStore(ILogger<DefaultRoleStore> logger) => _logger = logger;

    public async Task<IReadOnlyList<ISoraAuthRole>> All(CancellationToken ct = default)
        => (await Role.All(ct).ConfigureAwait(false)).Cast<ISoraAuthRole>().ToArray();

    public async Task UpsertMany(IEnumerable<ISoraAuthRole> items, CancellationToken ct = default)
    {
        var mapped = items.Select(x => new Role
        {
            Id = x.Id,
            Display = x.Display,
            Description = x.Description,
            RowVersion = x.RowVersion
        }).ToArray();
        _ = await Role.UpsertMany(mapped, ct).ConfigureAwait(false);
    }

    public Task<bool> Delete(string id, CancellationToken ct = default)
    => Role.Remove(id: id, ct: ct);
}

public sealed class DefaultRoleAliasStore : IRoleAliasStore
{
    private readonly ILogger<DefaultRoleAliasStore> _logger;
    public DefaultRoleAliasStore(ILogger<DefaultRoleAliasStore> logger) => _logger = logger;

    public async Task<IReadOnlyList<ISoraAuthRoleAlias>> All(CancellationToken ct = default)
        => (await RoleAlias.All(ct).ConfigureAwait(false)).Cast<ISoraAuthRoleAlias>().ToArray();

    public async Task UpsertMany(IEnumerable<ISoraAuthRoleAlias> items, CancellationToken ct = default)
    {
        var mapped = items.Select(x => new RoleAlias
        {
            Id = x.Id,
            TargetRole = x.TargetRole,
            RowVersion = x.RowVersion
        }).ToArray();
        _ = await RoleAlias.UpsertMany(mapped, ct).ConfigureAwait(false);
    }

    public Task<bool> Delete(string id, CancellationToken ct = default)
    => RoleAlias.Remove(id: id, ct: ct);
}

public sealed class DefaultRolePolicyBindingStore : IRolePolicyBindingStore
{
    private readonly ILogger<DefaultRolePolicyBindingStore> _logger;
    public DefaultRolePolicyBindingStore(ILogger<DefaultRolePolicyBindingStore> logger) => _logger = logger;

    public async Task<IReadOnlyList<ISoraAuthRolePolicyBinding>> All(CancellationToken ct = default)
        => (await RolePolicyBinding.All(ct).ConfigureAwait(false)).Cast<ISoraAuthRolePolicyBinding>().ToArray();

    public async Task UpsertMany(IEnumerable<ISoraAuthRolePolicyBinding> items, CancellationToken ct = default)
    {
        var mapped = items.Select(x => new RolePolicyBinding
        {
            Id = x.Id,
            Requirement = x.Requirement,
            RowVersion = x.RowVersion
        }).ToArray();
        _ = await RolePolicyBinding.UpsertMany(mapped, ct).ConfigureAwait(false);
    }

    public Task<bool> Delete(string id, CancellationToken ct = default)
    => RolePolicyBinding.Remove(id: id, ct: ct);
}
