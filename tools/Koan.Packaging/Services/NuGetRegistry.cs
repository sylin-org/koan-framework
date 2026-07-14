using System.Net;
using System.Text.Json;
using Koan.Packaging.Infrastructure;

namespace Koan.Packaging.Services;

internal sealed class NuGetRegistry(HttpClient httpClient)
{
    public async Task<bool> ExistsAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var url = PackageUrl(packageId, version);
        for (var attempt = 1; attempt <= PackagingConstants.RegistryHttpAttempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
                {
                    using var fallback = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (IsTransient(fallback.StatusCode) && attempt < PackagingConstants.RegistryHttpAttempts)
                    {
                        await BackoffAsync(attempt, cancellationToken);
                        continue;
                    }
                    if (fallback.StatusCode == HttpStatusCode.NotFound) return false;
                    fallback.EnsureSuccessStatusCode();
                    return true;
                }

                if (response.StatusCode == HttpStatusCode.NotFound) return false;
                if (IsTransient(response.StatusCode) && attempt < PackagingConstants.RegistryHttpAttempts)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (HttpRequestException) when (attempt < PackagingConstants.RegistryHttpAttempts)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
        throw new InvalidOperationException($"Unable to query nuget.org for {packageId}/{version}.");
    }

    public async Task DownloadAsync(string packageId, string version, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= PackagingConstants.RegistryHttpAttempts; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync(PackageUrl(packageId, version), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < PackagingConstants.RegistryHttpAttempts)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(destination);
                await source.CopyToAsync(target, cancellationToken);
                return;
            }
            catch (HttpRequestException) when (attempt < PackagingConstants.RegistryHttpAttempts)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
        throw new InvalidOperationException($"Unable to download {packageId}/{version} from nuget.org.");
    }

    public async Task<string> GetLatestStableVersionAsync(string packageId, CancellationToken cancellationToken)
    {
        var id = packageId.ToLowerInvariant();
        for (var attempt = 1; attempt <= PackagingConstants.RegistryHttpAttempts; attempt++)
        {
            try
            {
                using var response = await httpClient.GetAsync($"{PackagingConstants.NuGetFlatContainer}/{id}/index.json", cancellationToken);
                if (IsTransient(response.StatusCode) && attempt < PackagingConstants.RegistryHttpAttempts)
                {
                    await BackoffAsync(attempt, cancellationToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var versions = document.RootElement.GetProperty("versions").EnumerateArray()
                    .Select(element => element.GetString())
                    .Where(version => !string.IsNullOrWhiteSpace(version) && !version.Contains('-', StringComparison.Ordinal))
                    .Cast<string>()
                    .ToArray();
                if (versions.Length == 0) throw new InvalidOperationException($"No stable version of {packageId} exists on nuget.org.");
                return versions[^1];
            }
            catch (HttpRequestException) when (attempt < PackagingConstants.RegistryHttpAttempts)
            {
                await BackoffAsync(attempt, cancellationToken);
            }
        }
        throw new InvalidOperationException($"Unable to query stable versions of {packageId} from nuget.org.");
    }

    private static string PackageUrl(string packageId, string version)
    {
        var id = packageId.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();
        return $"{PackagingConstants.NuGetFlatContainer}/{id}/{normalizedVersion}/{id}.{normalizedVersion}.nupkg";
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static Task BackoffAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
}
