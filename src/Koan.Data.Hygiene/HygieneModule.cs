using Koan.Core;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Hygiene;

/// <summary>
/// Activates field hygiene: declarative <c>[Trim]</c> / <c>[Lowercase]</c> / <c>[Uppercase]</c>
/// normalization on Entity string properties, applied to the persisted clone on every write path.
/// Reference = Intent — referencing the package is the whole step.
/// </summary>
public sealed class HygieneModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFieldTransformContributor,
            HygieneFieldTransformContributor>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Hygiene", b => b.Value(
            "annotated string properties ([Trim]/[Lowercase]/[Uppercase]) normalized on the persisted clone; caller instances untouched"));
    }
}
