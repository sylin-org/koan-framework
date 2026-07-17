using Koan.Classification.Crypto;
using Koan.Core;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Classification.Initialization;

/// <summary>
/// Lights classification up when <c>Koan.Classification</c> is referenced (Reference = Intent, ARCH-0098). The
/// data core stays classification-agnostic: this module registers the crypto services and, once DI is available
/// (<see cref="Start"/>), registers a generic <see cref="FieldTransformContributor"/> into the data-core
/// field-transform seam and activates the classified-field registry. Not referencing the module leaves both seams
/// empty (structural no-op); a referencing app with no <c>[Classified]</c> property pays nothing (the contributor
/// returns <c>null</c> for every type).
/// </summary>
public sealed class ClassificationModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Env-tiered crypto seam (defaults; production replaces IKeyProvider with the persisted/KMS provider).
        services.TryAddSingleton<IFieldCipher, AesGcmFieldCipher>();
        services.TryAddSingleton<IKeyProvider, EphemeralKeyProvider>();
        services.TryAddSingleton<IClassificationTenantAccessor, NullClassificationTenantAccessor>();
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        // Activate the FACTS scan, then register the HANDLING contributor (one transform per type that has any
        // classified field; null otherwise so unclassified types keep the byte-identical fast path). The transform
        // resolves its crypto dependencies from the running host per operation (the plan is memoized process-globally),
        // so nothing host-specific is captured here.
        ClassifiedFieldRegistry.Activate();
        StorageFieldTransformRegistry.Register(new FieldTransformContributor(
            "classification",
            type =>
            {
                var bag = ClassifiedFieldRegistry.ForType(type);
                return bag.HasClassifiedFields ? new ClassificationFieldTransform(bag) : null;
            }));

        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var keyTier = env.IsDevelopment()
            ? "ephemeral (in-memory)"
            : "ephemeral (in-memory) — replace IKeyProvider with a persisted/KMS provider for production";
        module.SetSetting("Classification", b => b.Value($"cipher=AES-256-GCM; keys={keyTier}"));
    }
}
