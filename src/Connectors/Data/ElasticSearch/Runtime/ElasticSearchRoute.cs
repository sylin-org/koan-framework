using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.ElasticSearch;

internal sealed record ElasticSearchRoute(
    string Source,
    Uri Endpoint,
    string? ApiKey,
    string? Username,
    string? Password,
    DataSourcePlan Policy)
{
    internal static ElasticSearchRoute Create(
        string source,
        string endpoint,
        string? apiKey,
        string? username,
        string? password,
        DataSourcePlan policy) =>
        new(source, NormalizeEndpoint(endpoint), apiKey, username, password, policy);

    internal static Uri NormalizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"Elasticsearch endpoint '{endpoint}' must be an absolute HTTP or HTTPS URI.");
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}
