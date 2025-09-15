namespace Koan.Core.BackgroundServices;

/// <summary>
/// Base record for service commands
/// </summary>
public abstract record ServiceCommand
{
    public string ServiceName { get; init; } = "";
    public string CorrelationId { get; init; } = Guid.CreateVersion7().ToString("N");
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Command to trigger a service immediately
/// </summary>
public record TriggerNowCommand : ServiceCommand;

/// <summary>
/// Command to check service queue for new work
/// </summary>
public record CheckQueueCommand : ServiceCommand;

/// <summary>
/// Command to process a batch with specific parameters
/// </summary>
public record ProcessBatchCommand : ServiceCommand
{
    public int? BatchSize { get; init; }
    public string? Filter { get; init; }
}

/// <summary>
/// Command to perform a health check
/// </summary>
public record HealthCheckCommand : ServiceCommand;

/// <summary>
/// Command to get service status
/// </summary>
public record StatusCommand : ServiceCommand;