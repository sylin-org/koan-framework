using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Vector.Connector.Qdrant;

internal sealed record QdrantRoute(string Source, Uri Endpoint, string? ApiKey, DataSourcePlan Policy)
{
    internal static QdrantRoute Create(string source, string endpoint, string? apiKey, DataSourcePlan policy) =>
        new(source, NormalizeEndpoint(endpoint), apiKey, policy);

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Qdrant endpoint '{endpoint}' must be an absolute HTTP or HTTPS URI.");
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/", Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
    }
}
