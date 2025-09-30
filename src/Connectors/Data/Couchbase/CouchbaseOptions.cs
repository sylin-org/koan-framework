using System;
using System.ComponentModel.DataAnnotations;
using Koan.Core.Adapters;
using Koan.Core.Adapters.Configuration;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Connector.Couchbase;

/// <summary>
/// Couchbase adapter configuration options.
/// </summary>
public sealed class CouchbaseOptions : IAdapterOptions
{
    /// <summary>
    /// Couchbase connection string. Supports "auto" orchestration discovery by default.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "auto";

    /// <summary>
    /// Optional username for cluster authentication. When omitted, SDK defaults apply.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Optional password for cluster authentication.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Required bucket name housing application data.
    /// </summary>
    [Required]
    public string Bucket { get; set; } = "Koan";

    /// <summary>
    /// Optional scope name; defaults to the Couchbase default scope.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Optional static collection name override.
    /// </summary>
    public string? Collection { get; set; }

    /// <summary>
    /// Optional callback for dynamic collection naming per entity type.
    /// </summary>
    public Func<Type, string?>? CollectionName { get; set; }

    /// <summary>
    /// Naming convention for dynamically computed collection names.
    /// </summary>
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;

    /// <summary>
    /// Separator used when composing namespace + entity for default naming.
    /// </summary>
    public string Separator { get; set; } = ".";

    /// <summary>
    /// Default server-side page size guardrail applied when DataQueryOptions are not specified.
    /// </summary>
    public int DefaultPageSize { get; set; } = 50;

    /// <summary>
    /// Maximum server-side page size.
    /// </summary>
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Optional timeout for N1QL queries.
    /// </summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Optional durability level for mutations.
    /// </summary>
    public string? DurabilityLevel { get; set; }

    /// <summary>
    /// Readiness policy controlling adapter gating behaviour.
    /// </summary>
    public IAdapterReadinessConfiguration Readiness { get; set; } = new AdapterReadinessConfiguration();
}

