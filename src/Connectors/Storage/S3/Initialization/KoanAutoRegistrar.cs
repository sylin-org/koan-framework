using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Storage.Abstractions;
using Koan.Storage.Connector.S3.Infrastructure;

namespace Koan.Storage.Connector.S3.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Storage.Connector.S3";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Bind S3 provider options
        services.AddKoanOptions<S3StorageOptions>(S3StorageConstants.Configuration.Section);

        // Register the options configurator (explicit config only — no offering binding)
        // Storage is a first-class garden concept, not an offering.
        // Endpoint resolution happens lazily at first use via ZenGarden.Client.BoundEndpoint
        // + S3 port catalog query. No IZenGardenOfferingBinding needed.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<S3StorageOptions>, S3StorageOptionsConfigurator>());

        // Register the storage provider
        services.AddSingleton<IStorageProvider, S3StorageProvider>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        var endpoint = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:{S3StorageConstants.Configuration.Keys.Endpoint}",
            string.Empty) ?? string.Empty;

        var bucketPrefix = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:{S3StorageConstants.Configuration.Keys.BucketPrefix}",
            string.Empty) ?? string.Empty;

        module.AddSetting("Endpoint", string.IsNullOrWhiteSpace(endpoint) ? "(zen-garden auto)" : endpoint);
        module.AddSetting("BucketPrefix", string.IsNullOrWhiteSpace(bucketPrefix) ? "(from AppIdentity)" : bucketPrefix);
        var mossEndpoint = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:MossEndpoint",
            string.Empty) ?? string.Empty;

        module.AddSetting("MossEndpoint", string.IsNullOrWhiteSpace(mossEndpoint) ? "(auto from zen-garden)" : mossEndpoint);
        module.AddSetting("Capabilities", $"seek=true, range=true, presign={!string.IsNullOrWhiteSpace(mossEndpoint)}, copy=true, list=true");
    }
}
