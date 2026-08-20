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

        // The shared law: refuses in Production without consent, warns in Staging/Test/CI, silent in
        // Development. Spelling that out by hand cost a bug already -- an earlier version of this gate
        // read !IsDevelopment(), which turned a production safety rail into a functionality block that
        // broke Test, Staging, and CI.
        if (localCustody)
            KoanEnv.Gate.Enforce(new KoanMagic(
                Capability: "a local-custody key",
                Risk: "the built-in providers keep key material beside the data it protects and never rotate it, "
                    + "so anyone who can read the database can read the keyring.",
                Remedy: $"register an {nameof(IClassificationKeyProvider)} backed by your key service before "
                    + "AddKoan() completes composition"),
                environment, logger);

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
