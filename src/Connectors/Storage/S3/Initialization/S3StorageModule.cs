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

public sealed class S3StorageModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Bind S3 provider options
        services.AddKoanOptions<S3StorageOptions>(S3StorageConstants.Configuration.Section);

        // Register the options configurator (explicit config only — no offering binding)
        // Storage is a first-class garden concept, not an offering.
        // Endpoint resolution happens lazily at first use via ZenGarden.Client.BoundEndpoint
        // + S3 port catalog query; offering discovery is intentionally not involved.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<S3StorageOptions>, S3StorageOptionsConfigurator>());

        // Register the storage provider
        services.AddSingleton<IStorageProvider, S3StorageProvider>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);

        var endpoint = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:{S3StorageConstants.Configuration.Keys.Endpoint}",
            "") ?? "";

        var bucketPrefix = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:{S3StorageConstants.Configuration.Keys.BucketPrefix}",
            "") ?? "";

        module.AddSetting("Endpoint", string.IsNullOrWhiteSpace(endpoint) ? "(zen-garden auto)" : endpoint);
        module.AddSetting("BucketPrefix", string.IsNullOrWhiteSpace(bucketPrefix) ? "(from AppIdentity)" : bucketPrefix);
        var mossEndpoint = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:MossEndpoint",
            "") ?? "";

        module.AddSetting("MossEndpoint", string.IsNullOrWhiteSpace(mossEndpoint) ? "(auto from zen-garden)" : mossEndpoint);
        module.AddSetting("Capabilities", $"seek=true, range=true, presign={!string.IsNullOrWhiteSpace(mossEndpoint)}, copy=true, list=true");
    }
}
