namespace Koan.Web.Auth.Services.Discovery;

public interface IServiceDiscovery
{
    Task<ServiceEndpoint> ResolveServiceAsync(string serviceId, CancellationToken ct = default);
    Task<ServiceEndpoint[]> DiscoverServicesAsync(CancellationToken ct = default);
    Task RegisterServiceAsync(ServiceRegistration registration, CancellationToken ct = default);
}

public record ServiceEndpoint(string ServiceId, Uri BaseUrl, string[] SupportedScopes);

public record ServiceRegistration(string ServiceId, Uri BaseUrl, string[] ProvidedScopes);