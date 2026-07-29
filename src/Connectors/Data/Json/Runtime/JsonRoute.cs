using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Connector.Json.Runtime;

/// <summary>One canonical, policy-bearing JSON source route. Resolving it never touches storage.</summary>
internal sealed record JsonRoute(string Source, string DirectoryPath, DataSourcePlan Policy)
{
    internal static JsonRoute Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        JsonDataOptions defaults,
        IAdapterFactory owner,
        string source)
    {
        var resolvedSource = string.IsNullOrWhiteSpace(source)
            ? Infrastructure.Constants.Provider.DefaultSource
            : source;
        var configured = AdapterConnectionResolver.GetSourceSetting(
            configuration,
            sources,
            Infrastructure.Constants.Provider.Name,
            resolvedSource,
            Infrastructure.Constants.Configuration.DirectoryPath,
            defaults.DirectoryPath,
            owner);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"JSON directory is not configured for source '{resolvedSource}'. Set " +
                $"{Infrastructure.Constants.Configuration.Section}:DirectoryPath or the source-specific json:DirectoryPath.");
        }

        var directory = Path.GetFullPath(configured);
        return new JsonRoute(
            resolvedSource,
            directory,
            sources.GetPlan(resolvedSource, Infrastructure.Constants.Provider.Name, directory));
    }

    internal string FileFor(string physicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalName);
        var fileStem = physicalName.Replace(':', '.');
        if (fileStem is "." or ".." ||
            !string.Equals(Path.GetFileName(fileStem), fileStem, StringComparison.Ordinal) ||
            fileStem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException(
                $"JSON storage name '{physicalName}' is not a safe file name. Choose an entity/partition name without path characters.");
        }

        var path = Path.GetFullPath(Path.Combine(
            DirectoryPath,
            fileStem + Infrastructure.Constants.Storage.Extension));
        if (!JsonFileRegistry.PathComparer.Equals(Path.GetDirectoryName(path), DirectoryPath))
        {
            throw new InvalidOperationException(
                $"JSON storage name '{physicalName}' resolves outside source directory '{DirectoryPath}'.");
        }
        return path;
    }
}
