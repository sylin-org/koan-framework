using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Vector.Infrastructure;

internal static class VectorProvenanceItems
{
    private static readonly IReadOnlyCollection<string> DefaultProviderConsumers = new[]
    {
        "Koan.Data.Vector.VectorService"
    };

    internal static readonly ProvenanceItem DefaultProvider = new(
        "Koan:Data:VectorDefaults:DefaultProvider",
        "Default Vector Provider",
        "Fallback vector provider identifier used when requests omit an explicit target.",
        DefaultConsumers: DefaultProviderConsumers);
}
