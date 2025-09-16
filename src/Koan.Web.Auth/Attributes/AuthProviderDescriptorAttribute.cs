using Koan.Web.Auth.Infrastructure;
using System;

namespace Koan.Web.Auth.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class AuthProviderDescriptorAttribute : Attribute
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Protocol { get; }
    public string? Icon { get; init; }

    public AuthProviderDescriptorAttribute(string id, string displayName, string protocol)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Protocol = string.IsNullOrWhiteSpace(protocol) ? AuthConstants.Protocols.Oidc : protocol;
    }
}
