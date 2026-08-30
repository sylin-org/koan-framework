using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Vector.Connector.Chroma;

internal sealed record ChromaRoute(string Source, Uri Endpoint, string Tenant, string Database, string? ApiKey, DataSourcePlan Policy)
{
    internal static ChromaRoute Create(
        string source, string endpoint, string tenant, string database, string? apiKey, DataSourcePlan policy)
    {
        if (string.IsNullOrWhiteSpace(tenant)) throw new InvalidOperationException("Chroma tenant must not be blank.");
        if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("Chroma database must not be blank.");
        return new ChromaRoute(source, NormalizeEndpoint(endpoint), tenant, database, apiKey, policy);
    }

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Chroma endpoint '{endpoint}' must be an absolute HTTP or HTTPS URI.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/", Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
    }
}
