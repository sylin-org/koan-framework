using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Vector.Connector.Weaviate;

internal sealed record WeaviateRoute(string Source, Uri Endpoint, string? ApiKey, DataSourcePlan Policy)
{
    internal static WeaviateRoute Create(string source, string endpoint, string? apiKey, DataSourcePlan policy) =>
        new(source, NormalizeEndpoint(endpoint), apiKey, policy);

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Weaviate endpoint must be an absolute HTTP or HTTPS URI.");
        return new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }
}
