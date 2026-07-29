using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Vector.Connector.Milvus;

internal sealed record MilvusRoute(
    string Source,
    Uri Endpoint,
    string Database,
    string? Token,
    DataSourcePlan Policy)
{
    internal static MilvusRoute Create(
        string source,
        string endpoint,
        string database,
        string? token,
        DataSourcePlan policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        return new(source, NormalizeEndpoint(endpoint), database.Trim(), token, policy);
    }

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Milvus endpoint must be an absolute HTTP or HTTPS URI.");
        return new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }
}
