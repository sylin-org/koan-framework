namespace Koan.Web.Auth.Services.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class KoanServiceAttribute : Attribute
{
    public string ServiceId { get; }
    public string[] ProvidedScopes { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = string.Empty;

    public KoanServiceAttribute(string serviceId)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            throw new ArgumentException("Service ID cannot be null or empty", nameof(serviceId));

        ServiceId = serviceId;
    }
}