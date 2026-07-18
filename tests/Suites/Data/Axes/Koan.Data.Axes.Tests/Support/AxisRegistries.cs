using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Routing;

namespace Koan.Data.Axes.Tests.Support;

/// <summary>Reset the process-global static registries the expander / diagnostics read, plus the expander's cross-host
/// field-ownership ledger — used in spec ctor/dispose so each unit spec sees a clean slate (mirrors the data-core
/// pipeline specs).</summary>
internal static class AxisRegistries
{
    public static void ResetAll()
    {
        ManagedFieldRegistry.Reset();
        StorageNameParticleRegistry.Reset();
        OperationOverrideRegistry.Reset();
        DatabaseRouteRegistry.Reset();
        DataAxisExpander.ResetForTesting();
    }
}
