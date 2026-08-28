using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.DuckDb;

public sealed class DuckDbOptions : IAdapterOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto";
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = ".";
    public RelationalDdlPolicy DdlPolicy { get; set; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; set; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; set; }
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();

    /// <summary>
    /// Engine memory budget forwarded as DuckDB's <c>memory_limit</c>. Unset leaves DuckDB's default
    /// (80% of system RAM), which in-process competes with the application's own heap — embedded hosts
    /// should almost always set this.
    /// </summary>
    public string? MemoryLimit { get; set; }

    /// <summary>Engine thread count forwarded as DuckDB's <c>threads</c>. Unset uses all cores.</summary>
    public int? Threads { get; set; }

    /// <summary>
    /// Runtime extension auto-install is disabled by default: an embedded engine downloading binaries
    /// from a CDN at first use is a supply-chain and air-gap decision, not a default (DATA-0123).
    /// Pre-install extensions and point <see cref="ExtensionDirectory"/> at them instead.
    /// </summary>
    public bool AutoInstallExtensions { get; set; }

    /// <summary>Local directory preloaded with extension binaries, forwarded as <c>extension_directory</c>.</summary>
    public string? ExtensionDirectory { get; set; }

    /// <summary>
    /// Extensions to <c>LOAD</c> into the engine instance before first use — <c>sqlite</c>,
    /// <c>httpfs</c>, <c>iceberg</c>, ... — declared, not discovered. Each name must be a plain
    /// identifier; an extension that cannot load refuses with a corrective naming it and the
    /// pre-install/autoinstall choice. Loaded once per engine instance; the load persists for every
    /// later connection in the process.
    /// </summary>
    public List<string> Extensions { get; set; } = [];
}
