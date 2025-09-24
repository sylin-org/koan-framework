namespace Koan.Web.Auth.Services.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class CallsServiceAttribute : Attribute
{
    public string ServiceId { get; }
    public string[] RequiredScopes { get; init; } = Array.Empty<string>();
    public bool Optional { get; init; } = false;

    public CallsServiceAttribute(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        ServiceId = serviceId;
    }
}