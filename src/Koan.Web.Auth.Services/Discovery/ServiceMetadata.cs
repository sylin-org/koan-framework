using System.Reflection;

namespace Koan.Web.Auth.Services.Discovery;

public record ServiceMetadata(
    string ServiceId,
    string[] ProvidedScopes,
    ServiceDependency[] Dependencies,
    Type ControllerType
);

public record ServiceDependency(
    string ServiceId,
    string[] RequiredScopes,
    bool Optional
);

public class ServiceDiscoveryException : Exception
{
    public string ServiceId { get; }

    public ServiceDiscoveryException(string serviceId, string message) : base(message)
        => ServiceId = serviceId;

    public ServiceDiscoveryException(string serviceId, string message, Exception innerException) : base(message, innerException)
        => ServiceId = serviceId;
}