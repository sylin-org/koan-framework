using System.ComponentModel.DataAnnotations;

namespace Sora.Data.Cqrs;

/// <summary>
/// CQRS configuration bound from Sora:Cqrs.
/// </summary>
public sealed class CqrsOptions
{
    public string? DefaultProfile { get; set; }
    public Dictionary<string, CqrsProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CqrsProfile
{
    [Required]
    public Dictionary<string, CqrsEntityRoute> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public CqrsMessaging Messaging { get; set; } = new();
}

public sealed class CqrsEntityRoute
{
    [Required]
    public CqrsEndpoint Write { get; set; } = new();
    [Required]
    public CqrsEndpoint Read { get; set; } = new();
}

public sealed class CqrsEndpoint
{
    [Required]
    public string Provider { get; set; } = string.Empty; // e.g., "sqlite", "mongo"
    public string? ConnectionString { get; set; }
    public string? ConnectionStringName { get; set; }
    public Dictionary<string, object?>? Extras { get; set; }
}

public sealed class CqrsMessaging
{
    public string? Transport { get; set; } // e.g., "RabbitMq"
    public Dictionary<string, string>? Settings { get; set; } // e.g., { ConnectionStringName = "Rabbit" }
}
