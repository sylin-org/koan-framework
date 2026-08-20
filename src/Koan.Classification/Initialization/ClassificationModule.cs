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
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Classification");
        var ephemeral = provider is EphemeralClassificationKeyProvider;

        // Production is the gate, not Development. Koan's convention is that a capability works from a
        // bare reference everywhere and asks for explicit consent only in Production -- the same shape
        // as AllowProductionDdl, and honoring the framework-wide Koan:AllowMagicInProduction escape
        // hatch. Gating on !IsDevelopment() instead turned a production safety rail into a functionality
        // block that broke Test and Staging, including CI.
        if (ephemeral && environment.IsProduction() && !KoanEnv.AllowMagicInProduction)
            throw new InvalidOperationException(
                $"Koan Classification refuses ephemeral keys in environment '{environment.EnvironmentName}'. " +
                $"An ephemeral key is regenerated per process, so data encrypted with it cannot be read after a " +
                $"restart. Register a durable {nameof(IClassificationKeyProvider)} before AddKoan() completes composition.");

        // Loud outside Development, because an ephemeral key is a real data-loss boundary the moment
        // anything persists across a restart -- but it is a warning to act on, not a wall.
        if (ephemeral && !environment.IsDevelopment())
            logger?.LogWarning(
                "Classification is using an ephemeral key in environment '{Environment}'. Keys are regenerated per " +
                "process, so anything encrypted now becomes unreadable after a restart. Register a durable {Contract} " +
                "before this reaches production.",
                environment.EnvironmentName,
                nameof(IClassificationKeyProvider));

        logger?.LogInformation(
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
