using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.OpenSearch;

internal sealed record OpenSearchRoute(
    string Source,
    Uri Endpoint,
    string? ApiKey,
    string? Username,
    string? Password,
    DataSourcePlan Policy)
{
    internal static OpenSearchRoute Create(
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
                $"OpenSearch endpoint '{endpoint}' must be an absolute HTTP or HTTPS URI.");
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}
