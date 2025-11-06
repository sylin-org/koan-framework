using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Koan.Core;

namespace Koan.Web;

/// <summary>
/// Web-specific infrastructure utilities for Koan Framework applications.
/// Mirrors the KoanEnv pattern for environment detection, providing web-specific capabilities.
/// </summary>
public static class KoanWeb
{
    /// <summary>
    /// URL resolution utilities for container-aware web applications.
    /// </summary>
    public static class Urls
    {
        /// <summary>
        /// Gets the application base URL suitable for display in logs and boot reports.
        /// Uses localhost replacements for container-internal wildcards (0.0.0.0, +, *).
        /// </summary>
        /// <param name="cfg">Configuration instance (optional)</param>
        /// <param name="env">Host environment (optional)</param>
        /// <returns>Base URL like "http://localhost:5080" or configured override</returns>
        public static string Base(IConfiguration? cfg = null, IHostEnvironment? env = null)
        {
            // 1. Explicit configuration override (highest priority)
            var configUrl = Configuration.Read<string?>(cfg, Infrastructure.ConfigurationConstants.Web.Keys.ApplicationUrl, null);
            if (!string.IsNullOrEmpty(configUrl))
            {
                return NormalizeUrl(configUrl);
            }

            // 2. ASPNETCORE_URLS environment variable (most reliable during startup)
            var aspNetCoreUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
                               ?? Configuration.Read<string?>(cfg, "ASPNETCORE_URLS", null);
            if (!string.IsNullOrEmpty(aspNetCoreUrls))
            {
                return ParseAspNetCoreUrls(aspNetCoreUrls);
            }

            // 3. Fallback based on environment detection
            // Container default: 5080 (common Koan convention)
            // Development default: 5000 (ASP.NET Core default)
            return KoanEnv.InContainer ? "http://localhost:5080" : "http://localhost:5000";
        }

        /// <summary>
        /// Builds a full URL from a relative path.
        /// </summary>
        /// <param name="path">Relative path like "/swagger" or ".koan"</param>
        /// <param name="cfg">Configuration instance (optional)</param>
        /// <param name="env">Host environment (optional)</param>
        /// <returns>Full URL like "http://localhost:5080/swagger"</returns>
        public static string Build(string path, IConfiguration? cfg = null, IHostEnvironment? env = null)
        {
            var baseUrl = Base(cfg, env);
            var normalizedPath = path.TrimStart('/');
            return $"{baseUrl}/{normalizedPath}";
        }

        private static string ParseAspNetCoreUrls(string aspNetCoreUrls)
        {
            // Parse first URL from semicolon-separated list
            var parts = aspNetCoreUrls.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var firstUrl = parts.FirstOrDefault(p => p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                                   || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                          ?? parts.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(firstUrl))
            {
                return "http://localhost:5000"; // Ultimate fallback
            }

            // Replace Kestrel wildcards before parsing URI (Uri.TryCreate fails with +, *, etc.)
            var normalizedUrl = firstUrl
                .Replace("://+:", "://localhost:")
                .Replace("://*:", "://localhost:")
                .Replace("://0.0.0.0:", "://localhost:")
                .Replace("://[::]:", "://localhost:");

            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var baseUri))
            {
                return "http://localhost:5000"; // Ultimate fallback
            }

            return BuildUrlFromUri(baseUri);
        }

        private static string BuildUrlFromUri(Uri uri)
        {
            var scheme = uri.Scheme;
            var host = uri.Host;
            var port = uri.Port;

            // Replace container-internal wildcards with localhost
            if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0" || host == "+" || host == "*")
            {
                host = "localhost";
            }

            // Include port unless it's the default for the scheme
            var includePort = (scheme == Uri.UriSchemeHttp && port != 80)
                           || (scheme == Uri.UriSchemeHttps && port != 443);

            return includePort ? $"{scheme}://{host}:{port}" : $"{scheme}://{host}";
        }

        private static string NormalizeUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url.TrimEnd('/'); // Return as-is if not parseable
            }

            return BuildUrlFromUri(uri).TrimEnd('/');
        }
    }
}
