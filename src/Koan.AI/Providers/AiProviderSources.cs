using System.ComponentModel;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Providers;

/// <summary>AI-owned construction and validation for ordinary endpoint-backed provider sources.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AiProviderSources
{
    public static AiSourceDefinition Create(
        string provider,
        IEnumerable<string> endpoints,
        IReadOnlyDictionary<string, AiCapabilityConfig> capabilities,
        string origin,
        bool isAutoDiscovered,
        string policy = "Fallback",
        int priority = 50)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(origin);

        var providerId = provider.Trim().ToLowerInvariant();
        var normalized = endpoints
            .Select(NormalizeEndpoint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0)
        {
            throw new InvalidOperationException(
                $"AI provider '{providerId}' cannot publish a source without at least one endpoint.");
        }

        var members = normalized
            .Select((endpoint, index) => new AiMemberDefinition
            {
                Name = $"{providerId}::member-{index + 1}",
                ConnectionString = endpoint,
                Order = index,
                Capabilities = capabilities,
                Origin = origin,
                IsAutoDiscovered = isAutoDiscovered
            })
            .ToList();

        return new AiSourceDefinition
        {
            Name = providerId,
            Provider = providerId,
            Priority = priority,
            Policy = policy,
            Members = members,
            Capabilities = capabilities,
            Origin = origin,
            IsAutoDiscovered = isAutoDiscovered
        };
    }

    private static string NormalizeEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                $"AI provider endpoint '{value}' is invalid. Use an absolute HTTP or HTTPS URI.");
        }

        return endpoint.ToString().TrimEnd('/');
    }
}
