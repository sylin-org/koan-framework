using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Sqlite;

public sealed class SqliteOptions : IAdapterOptions
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
    /// The journal mode set on every writable file database the connector opens
    /// (<c>Koan:Data:Sqlite:JournalMode</c>). Default <see cref="SqliteJournalMode.Wal"/>.
    /// </summary>
    public SqliteJournalMode JournalMode { get; set; } = SqliteJournalMode.Wal;

    /// <summary>
    /// How long a connection waits for a lock another connection holds before it fails with <c>SQLITE_BUSY</c>
    /// (<c>Koan:Data:Sqlite:BusyTimeout</c>, a <see cref="TimeSpan"/>). It sets both SQLite's <c>busy_timeout</c> and
    /// the driver's command timeout, which retries a busy lock on its own; a value is rounded up to whole seconds for
    /// the driver. Default 30 seconds, the driver's own default.
    /// </summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The durability flush level set on every connection the connector opens (<c>Koan:Data:Sqlite:Synchronous</c>).
    /// Default <see cref="SqliteSynchronous.Full"/>; <see cref="SqliteSynchronous.Normal"/> trades the last
    /// transactions before a power loss for throughput under <see cref="SqliteJournalMode.Wal"/>.
    /// </summary>
    public SqliteSynchronous Synchronous { get; set; } = SqliteSynchronous.Full;
}
