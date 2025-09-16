namespace Koan.Web.Auth.Services.Http;

public interface IKoanServiceClient
{
    Task<T?> GetAsync<T>(string serviceId, string endpoint, CancellationToken ct = default) where T : class;
    Task<T?> PostAsync<T>(string serviceId, string endpoint, object? data = null, CancellationToken ct = default) where T : class;
    Task<HttpResponseMessage> SendAsync(string serviceId, HttpRequestMessage request, CancellationToken ct = default);
}

public interface IKoanServiceClient<TService> : IKoanServiceClient where TService : class
{
    // Typed client for specific service - provides compile-time service ID checking
}