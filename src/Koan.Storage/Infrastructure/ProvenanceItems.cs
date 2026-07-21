using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Storage.Infrastructure;

internal static class StorageProvenanceItems
{
    private static readonly IReadOnlyCollection<string> StorageConsumers = new[]
    {
        "Koan.Storage.StorageService"
    };

    internal static readonly ProvenanceItem Profiles = new(
        $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.Profiles}",
        "Storage Profiles",
        "Number of storage profiles configured for content and asset operations.",
        DefaultConsumers: StorageConsumers);

    internal static readonly ProvenanceItem DefaultProfile = new(
        $"{StorageConstants.Constants.Configuration.Section}:{StorageConstants.Constants.Configuration.Keys.DefaultProfile}",
        "Default Storage Profile",
        "Named storage profile used when callers omit an explicit profile.",
        DefaultConsumers: StorageConsumers);

}
