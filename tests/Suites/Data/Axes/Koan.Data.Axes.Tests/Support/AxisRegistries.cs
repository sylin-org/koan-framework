using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Pipeline;
using Koan.Data.Core.Routing;

namespace Koan.Data.Axes.Tests.Support;

/// <summary>Reset the current composition's host-owned declaration catalogs and field-ownership ledger.</summary>
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
