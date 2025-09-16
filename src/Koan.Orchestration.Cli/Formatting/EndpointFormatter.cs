using Koan.Orchestration;
using Koan.Orchestration.Abstractions;

namespace Koan.Orchestration.Cli.Formatting;

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
                                    ? ("tcp", null)
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
                                    ? ("tcp", null)
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

    // Heuristic fallback removed (ARCH-0049)
}
