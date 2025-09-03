using System.Globalization;
using System.Text.RegularExpressions;
using Sora.Messaging.Provisioning;

namespace Sora.Messaging;

/// <summary>
/// Default implementation of <see cref="ITopologyNaming"/> applying the patterns defined in ADR MESS-0070.
/// Converts aliases to lower-kebab-case and optionally appends .v{version}.
/// </summary>
internal sealed class DefaultTopologyNaming : ITopologyNaming
{
    private static readonly Regex _upper = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);

    public string CommandRouting(string targetService, string alias, int? version, bool includeVersion)
        => $"cmd.{Normalize(targetService)}.{Normalize(alias)}{VersionSuffix(version, includeVersion)}";

    public string AnnouncementRouting(string domain, string alias, int? version, bool includeVersion)
        => $"ann.{Normalize(domain)}.{Normalize(alias)}{VersionSuffix(version, includeVersion)}";

    public string FlowEventRouting(string adapter, string alias)
        => $"flow.{Normalize(adapter)}.{Normalize(alias)}";

    public string QueueFor(string routingKey, string group)
        => $"{routingKey}.q.{Normalize(group)}";

    public string DlqFor(string queueName)
        => queueName + ".dlq";

    private static string VersionSuffix(int? version, bool include)
        => include && version is > 0 ? $".v{version}" : string.Empty;

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var kebab = _upper.Replace(value, m => m.Groups[1].Value + "-" + m.Groups[2].Value);
        kebab = kebab.Replace('_', '-');
        return kebab.ToLowerInvariant();
    }
}
