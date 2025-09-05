using Sora.Data.Abstractions.Naming;
using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Mongo;

/// <summary>
/// MongoDB adapter options (connection string, database, and optional collection naming).
/// </summary>
public sealed class MongoOptions
{
    [Required]
    public string ConnectionString { get; set; } = "auto"; // DX-first: auto-detect by default
    [Required]
    public string Database { get; set; } = "sora";
    public Func<Type, string>? CollectionName { get; set; }
    // Naming policy controls
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = "."; // used when composing namespace + entity

    // Paging guardrails (acceptance criteria 0044)
    public int DefaultPageSize { get; set; } = 50; // mirrors Sora.Web default
    public int MaxPageSize { get; set; } = 200;
}