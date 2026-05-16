using System.ComponentModel.DataAnnotations;

namespace Koan.Data.Cqrs;

public sealed class CqrsEndpoint
{
    [Required]
    public string Provider { get; set; } = ""; // e.g., "sqlite", "mongo"
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public Dictionary<string, object?>? Extras { get; set; }
}