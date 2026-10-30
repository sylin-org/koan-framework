namespace Koan.Identity.Roles;

/// <summary>
/// A bound of the role model refused a change before it was written: adding a person who already holds
/// <see cref="RoleOptions.MaxRolesPerPerson"/> roles. The message names the bound and the correction;
/// <see cref="ConfigurationKey"/> is the key that sets it. A route answers it with 409; any other failure of the change,
/// a lifecycle handler's included, keeps its own exception.
/// </summary>
public sealed class RoleBoundException(string configurationKey, string message) : InvalidOperationException(message)
{
    /// <summary>The configuration key of the bound, for example <c>Koan:Identity:Roles:MaxRolesPerPerson</c>.</summary>
    public string ConfigurationKey { get; } = configurationKey;
}
