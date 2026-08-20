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
        // The floor is durable on purpose. An in-memory key regenerates every process, so the ordinary
        // run-stop-run loop would make everything written before the restart unreadable — a capability
        // that corrupts its own data by default is not a usable one. Local custody is still not
        // production custody; Start says so and gates Production.
        services.TryAddSingleton<IClassificationKeyProvider, LocalFileClassificationKeyProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IFieldTransformContributor,
            ClassificationFieldTransformContributor>());
    }

    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        var provider = services.GetRequiredService<IClassificationKeyProvider>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Classification");
        // Both built-ins are local custody: material lives beside the data, unrotated, protected only by
        // the host. Either is fine to develop against and neither is a production key service.
        var localCustody = provider is EphemeralClassificationKeyProvider or LocalFileClassificationKeyProvider;

        // Production is the gate, not Development. Koan's convention is that a capability works from a
        // bare reference everywhere and asks for explicit consent only in Production -- the same shape
        // as AllowProductionDdl, and honoring the framework-wide Koan:AllowMagicInProduction escape
        // hatch. Gating on !IsDevelopment() instead turned a production safety rail into a functionality
        // block that broke Test and Staging, including CI.
        if (localCustody && environment.IsProduction() && !KoanEnv.AllowMagicInProduction)
            throw new InvalidOperationException(
                $"Koan Classification refuses a local-custody key in environment '{environment.EnvironmentName}'. " +
                $"The built-in providers keep key material beside the data and never rotate it, which is a " +
                $"development posture. Register an {nameof(IClassificationKeyProvider)} backed by your key service " +
                $"before AddKoan() completes composition, or set Koan:AllowMagicInProduction to accept local custody.");

        // Loud outside Development, because an ephemeral key is a real data-loss boundary the moment
        // anything persists across a restart -- but it is a warning to act on, not a wall.
        if (localCustody && !environment.IsDevelopment())
            logger?.LogWarning(
                "Classification is using local key custody in environment '{Environment}' ({Provider}). Key material " +
                "sits beside the data it protects and is never rotated. Register an {Contract} backed by your key " +
                "service before this reaches production.",
                environment.EnvironmentName,
                provider.GetType().Name,
                nameof(IClassificationKeyProvider));

        // Name the custody, not just the type. "Which file holds my keys?" is the first question anyone asks
        // the moment classified data matters, and it should not require reading the source to answer.
        var custody = provider is LocalFileClassificationKeyProvider local
            ? $"local keyring at {local.KeyringPath}"
            : provider is EphemeralClassificationKeyProvider
                ? "in-memory, discarded on exit"
                : "application-supplied";
        logger?.LogInformation(
            "Classification field-at-rest protection active: cipher=AES-256-GCM; key-provider={Provider}; custody={Custody}; scope=compiled segmentation.",
            provider.GetType().FullName ?? provider.GetType().Name,
            custody);
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
