using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        module.AddSetting(S3StorageConstants.Configuration.Keys.Endpoint, string.IsNullOrWhiteSpace(endpoint) ? "(zen-garden auto)" : endpoint);
        module.AddSetting(S3StorageConstants.Configuration.Keys.BucketPrefix, string.IsNullOrWhiteSpace(bucketPrefix) ? "(from AppIdentity)" : bucketPrefix);
        var mossEndpoint = Core.Configuration.Read(
            cfg,
            $"{S3StorageConstants.Configuration.Section}:{S3StorageConstants.Configuration.Keys.MossEndpoint}",
            "") ?? "";

        module.AddSetting(S3StorageConstants.Configuration.Keys.MossEndpoint, string.IsNullOrWhiteSpace(mossEndpoint) ? "(auto from zen-garden)" : mossEndpoint);
    }
}
