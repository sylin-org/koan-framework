using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Core.Semantics;
using Koan.Data.Vector;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Composes the smallest runtime needed by manually assembled vector-adapter conformance hosts.
/// </summary>
public static class VectorAdapterTestServices
{
    public static IServiceCollection AddVectorAdapterTestRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Real applications receive these from Data Core through AddKoan(). Conformance hosts intentionally
        // assemble only the vector layer, so supply the neutral segmentation realization in one test chokepoint.
        services.TryAddSingleton(SegmentationPlan.Empty);
        services.TryAddSingleton<DataSegmentationPlan>();

        return services.AddKoanDataVector();
    }
}
