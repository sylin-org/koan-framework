using Koan.Data.Abstractions;

namespace Koan.Web.Auth.Roles.Contracts;

// Instance-shape-only contracts; statics live on default models for DX
public interface IKoanAuthRole : IEntity<string>
{
    string? Display { get; }
    string? Description { get; }
    byte[]? RowVersion { get; }
}

public interface IKoanAuthRoleAlias : IEntity<string>
{
    // Id is the alias key; TargetRole is the canonical role to map to
    string TargetRole { get; }
    byte[]? RowVersion { get; }
}

public interface IKoanAuthRolePolicyBinding : IEntity<string>
{
    // Id is the policy name; Requirement is like "role:admin" or "perm:auth.roles.admin"
    string Requirement { get; }
    byte[]? RowVersion { get; }
}

// Store contracts used by the management controller/service layer
public interface IRoleStore
{
    Task<IReadOnlyList<IKoanAuthRole>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<IKoanAuthRole> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}

public interface IRoleAliasStore
{
    Task<IReadOnlyList<IKoanAuthRoleAlias>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<IKoanAuthRoleAlias> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}

public interface IRolePolicyBindingStore
{
    Task<IReadOnlyList<IKoanAuthRolePolicyBinding>> All(CancellationToken ct = default);
    Task UpsertMany(IEnumerable<IKoanAuthRolePolicyBinding> items, CancellationToken ct = default);
    Task<bool> Delete(string id, CancellationToken ct = default);
}
