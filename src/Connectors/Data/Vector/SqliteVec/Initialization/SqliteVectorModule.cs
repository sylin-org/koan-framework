using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Vector.Connector.SqliteVec.Initialization;

/// <summary>
/// Auto-registers the sqlite-vec durable vector adapter (Reference = Intent). Reference this alongside the
/// SQLite data adapter and vectors persist in-process with no server; the native vec0 ships embedded.
/// </summary>
public sealed class SqliteVectorModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<SqliteVecOptions>(SqliteVecOptions.Section);
        services.AddSingleton<IVectorAdapterFactory, SqliteVecAdapterFactory>();
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var cs = cfg[$"{SqliteVecOptions.Section}:ConnectionString"] ?? "Data Source=koan-vectors.db";
        module.AddSetting("Vector", "sqlite-vec (vec0 v0.1.9, in-process)");
        module.AddSetting("Store", Koan.Core.Redaction.DeIdentify(cs));
        module.AddNote("Durable in-process vectors — native vec0 embedded, no server.");
    }
}
