using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Backup.Abstractions;
using Koan.Data.Backup.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Backup.Initialization;

public sealed class DataBackupModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.TryAddScoped<IBackupService, BackupService>();

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("Creates and restores one integrity-checked, provider-bounded Entity archive through Koan Storage.");
    }
}
