namespace Koan.Identity.Roles;

/// <summary>
/// A configured bound refused a role or overwrite change before it was written. The message names the bound and the
/// correction; <see cref="ConfigurationKey"/> is the key that sets it. A route answers it with 409; any other failure
/// of the change, a lifecycle handler's included, keeps its own exception.
/// </summary>
public sealed class RoleBoundException(string configurationKey, string message) : InvalidOperationException(message)
{
    /// <summary>The configuration key of the bound, for example <c>Koan:Identity:Roles:MaxMembersPerRole</c>.</summary>
    public string ConfigurationKey { get; } = configurationKey;
}
