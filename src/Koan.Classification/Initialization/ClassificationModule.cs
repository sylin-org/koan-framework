using Koan.Classification.Crypto;
using Koan.Classification.Pipeline;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Provenance;
using Koan.Data.Abstractions.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Classification.Initialization;

/// <summary>Composes local-first field-at-rest protection when Koan Classification is referenced.</summary>
public sealed class ClassificationModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.TryAddSingleton<IFieldCipher, AesGcmFieldCipher>();
        services.TryAddSingleton<IClassificationKeyProvider, EphemeralClassificationKeyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFieldTransformContributor,
            ClassificationFieldTransformContributor>());
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var provider = services.GetRequiredService<IClassificationKeyProvider>();
        if (!environment.IsDevelopment() && provider is EphemeralClassificationKeyProvider)
            throw new InvalidOperationException(
                $"Koan Classification refuses ephemeral keys in environment '{environment.EnvironmentName}'. " +
                $"Register a durable {nameof(IClassificationKeyProvider)} before AddKoan() completes composition.");

        services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Classification").LogInformation(
            "Classification field-at-rest protection active: cipher=AES-256-GCM; key-provider={Provider}; scope=compiled segmentation.",
            provider.GetType().FullName ?? provider.GetType().Name);
        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Classification", b => b.Value(
            "writable string properties; AES-256-GCM at rest; compiled segmentation key scope; distributed cache excluded"));
        module.SetSetting("Classification exclusions", b => b.Value(
            "no searchable ciphertext, tokenization, caller masking, backfill, message/log/vector redaction, or erasure"));
    }

    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
    {
        var provider = services.GetRequiredService<IClassificationKeyProvider>();
        var providerName = provider.GetType().FullName ?? provider.GetType().Name;
        composition.AddCapability(
            "classification:field-at-rest",
            [
                "aes-256-gcm",
                "string-properties",
                "segmentation-scoped",
                providerName,
            ]);
        composition.AddGuarantee(
            Infrastructure.Constants.Diagnostics.CapabilityCode,
            Infrastructure.Constants.Diagnostics.CapabilitySubject,
            $"Writable string fields use AES-256-GCM at rest through Data; scope derives from compiled segmentation; " +
            $"key-provider={providerName}; distributed Entity cache excluded. No search, masking, backfill, redaction, or erasure.",
            Infrastructure.Constants.Diagnostics.CapabilityReason,
            source: "Koan.Classification");
    }
}
