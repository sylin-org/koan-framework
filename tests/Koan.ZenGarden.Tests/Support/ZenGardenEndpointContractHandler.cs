using System.Text;
using Koan.ZenGarden.Core;

namespace Koan.ZenGarden.Tests.Support;

internal static class ZenGardenEndpointContractHandler
{
    public static string ResolveMongoEndpointOrThrow(ZenGardenOfferingResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        foreach (var uriText in resolution.Uris)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (string.Equals(uri.Scheme, "mongodb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "mongodb+srv", StringComparison.OrdinalIgnoreCase))
            {
                return uriText;
            }
        }

        if (resolution.Protocol is not null &&
            (string.Equals(resolution.Protocol, "mongodb", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resolution.Protocol, "mongodb+srv", StringComparison.OrdinalIgnoreCase)))
        {
            var host = FirstNonEmpty(resolution.Hostname, resolution.Ip);
            if (!string.IsNullOrWhiteSpace(host))
            {
                var port = resolution.Port is > 0 ? resolution.Port.Value : 27017;
                return $"mongodb://{host}:{port}";
            }
        }

        throw BuildContractException(
            resolution,
            "MongoDB offering is missing a MongoDB-native endpoint.",
            "Expected at least one connection URI with scheme 'mongodb' or 'mongodb+srv', or protocol='mongodb' with host/port.");
    }

    public static string ResolveOllamaEndpointOrThrow(ZenGardenOfferingResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        foreach (var uriText in resolution.Uris)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return uriText;
            }
        }

        if (resolution.Protocol is not null &&
            (string.Equals(resolution.Protocol, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(resolution.Protocol, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var host = FirstNonEmpty(resolution.Hostname, resolution.Ip);
            if (!string.IsNullOrWhiteSpace(host))
            {
                var port = resolution.Port is > 0 ? resolution.Port.Value : 11434;
                return $"{resolution.Protocol.ToLowerInvariant()}://{host}:{port}";
            }
        }

        throw BuildContractException(
            resolution,
            "Ollama offering is missing an HTTP endpoint.",
            "Expected at least one connection URI with scheme 'http' or 'https', or protocol='http|https' with host/port.");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static InvalidOperationException BuildContractException(
        ZenGardenOfferingResolution resolution,
        string summary,
        string expected)
    {
        var details = new StringBuilder();
        details.AppendLine(summary);
        details.AppendLine(expected);
        details.AppendLine("This should be corrected in Zen Garden tools-domain connection metadata, not worked around in client adapters.");
        details.AppendLine($"tool_fqid={resolution.ToolFqid}");
        details.AppendLine($"offering={resolution.Offering}");
        details.AppendLine($"instance={resolution.Instance ?? "(none)"}");
        details.AppendLine($"protocol={resolution.Protocol ?? "(none)"}");
        details.AppendLine($"hostname={resolution.Hostname ?? "(none)"}");
        details.AppendLine($"ip={resolution.Ip ?? "(none)"}");
        details.AppendLine($"port={(resolution.Port?.ToString() ?? "(none)")}");
        details.AppendLine($"uris=[{string.Join(", ", resolution.Uris)}]");
        return new InvalidOperationException(details.ToString());
    }
}
