using Sora.Orchestration;
using Sora.Orchestration.Abstractions;

namespace Sora.Orchestration.Cli.Formatting;

internal static class EndpointFormatter
{
    public static string FormatLiveEndpoint(PortBinding p)
    {
        var host = string.IsNullOrWhiteSpace(p.Address) || p.Address == "0.0.0.0" || p.Address == "::" ||
                   p.Address == "[::]"
            ? "localhost"
            : p.Address!.Contains(':') && !p.Address!.StartsWith('[') && !p.Address!.EndsWith(']')
                ? $"[{p.Address}]" // IPv6 literal
                : p.Address!;
        var (scheme, pattern) = _endpointResolver?.Invoke(p.Service, p.Container)
                                ?? (_resolver is null
                                    ? (InferSchemeByImage(p.Service, p.Container), null)
                                    : (_resolver.Invoke(p.Service, p.Container), null));
        var left = pattern is { Length: > 0 }
            ? pattern.Replace("{host}", host).Replace("{port}", p.Host.ToString())
            : $"{scheme}://{host}:{p.Host}";
        return $"{left} -> {p.Container} ({p.Protocol})";
    }

    // Render a plan-derived hint endpoint using (image/id, containerPort, hostPort)
    public static string GetPlanHint(string serviceIdOrImage, int containerPort, int hostPort)
    {
        var (scheme, pattern) = _endpointResolver?.Invoke(serviceIdOrImage, containerPort)
                                ?? (_resolver is null
                                    ? (InferSchemeByImage(serviceIdOrImage, containerPort), null)
                                    : (_resolver.Invoke(serviceIdOrImage, containerPort), null));
        return pattern is { Length: > 0 }
            ? pattern.Replace("{host}", "localhost").Replace("{port}", hostPort.ToString())
            : $"{scheme}://localhost:{hostPort}";
    }

    // Resolver is provided by the caller with (serviceIdOrImage, containerPort) → scheme
    private static Func<string, int, string>? _resolver;

    public static void UseSchemeResolver(Func<string, int, string> resolver)
    {
        _resolver = resolver;
    }

    // Enhanced resolver: returns (scheme, optional UriPattern)
    private static Func<string, int, (string Scheme, string? Pattern)>? _endpointResolver;

    public static void UseEndpointResolver(Func<string, int, (string Scheme, string? Pattern)> resolver)
    {
        _endpointResolver = resolver;
    }

    // Fallback: image-based heuristics; service string is expected to be image or id; prefer image prefixes.
    private static string InferSchemeByImage(string serviceIdOrImage, int containerPort)
    {
        var s = serviceIdOrImage.ToLowerInvariant();
        if (s.Contains("postgres")) return "postgres";
        if (s.Contains("redis")) return "redis";
        if (s.Contains("mongo")) return "mongodb";
        if (s.Contains("elastic") || s.Contains("opensearch")) return containerPort == 443 ? "https" : "http";
        if (containerPort == 443) return "https";
        return containerPort is 80 or 8080 or 3000 or 5000 or 5050 or 4200 or 9200 ? "http" : "tcp";
    }
}
