using Sora.Data.Abstractions;

namespace Sora.Web.Auth.Roles.Contracts;

// Instance-shape-only contracts; statics live on default models for DX
public interface ISoraAuthRole : IEntity<string>
{
    string? Display { get; }
    string? Description { get; }
    byte[]? RowVersion { get; }
}

public interface ISoraAuthRoleAlias : IEntity<string>
{
    // Id is the alias key; TargetRole is the canonical role to map to
    string TargetRole { get; }
    byte[]? RowVersion { get; }
}

public interface ISoraAuthRolePolicyBinding : IEntity<string>
{
    // Id is the policy name; Requirement is like "role:admin" or "perm:auth.roles.admin"
    string Requirement { get; }
    byte[]? RowVersion { get; }
}

// Store contracts used by the management controller/service layer
public interface IRoleStore
{
    Task<IReadOnlyList<ISoraAuthRole>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<ISoraAuthRole> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}

public interface IRoleAliasStore
{
    Task<IReadOnlyList<ISoraAuthRoleAlias>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<ISoraAuthRoleAlias> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}

public interface IRolePolicyBindingStore
{
    Task<IReadOnlyList<ISoraAuthRolePolicyBinding>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<ISoraAuthRolePolicyBinding> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}
