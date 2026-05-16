namespace Koan.Web.Auth.Services.Discovery;

public interface IServiceDiscovery
{
    Task<ServiceEndpoint> ResolveService(string serviceId, CancellationToken ct = default);
    Task<ServiceEndpoint[]> DiscoverServices(CancellationToken ct = default);
    Task RegisterService(ServiceRegistration registration, CancellationToken ct = default);
}

public record ServiceEndpoint(string ServiceId, Uri BaseUrl, string[] SupportedScopes);

public record ServiceRegistration(string ServiceId, Uri BaseUrl, string[] ProvidedScopes);