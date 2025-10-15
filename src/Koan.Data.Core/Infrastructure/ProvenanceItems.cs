using System.Collections.Generic;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Data.Core.Infrastructure;

internal static class DataCoreProvenanceItems
{
    private static readonly IReadOnlyCollection<string> SchemaLifecycleConsumers = new[]
    {
        "Koan.Data.Core.SchemaLifecycle"
    };

    internal static readonly ProvenanceItem EnsureSchemaOnStart = new(
        "Koan:Data:Runtime:EnsureSchemaOnStart",
        "Ensure Schema On Start",
        "Creates missing schema objects during startup to keep entity stores aligned.",
        DefaultConsumers: SchemaLifecycleConsumers);
}
