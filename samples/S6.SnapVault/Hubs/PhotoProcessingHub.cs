using Microsoft.AspNetCore.SignalR;

namespace S6.SnapVault.Hubs;

/// <summary>
/// SignalR hub for real-time photo processing updates
/// Clients connect to receive progress notifications as photos are processed
/// </summary>
public class PhotoProcessingHub : Hub
{
    private readonly ILogger<PhotoProcessingHub> _logger;

    public PhotoProcessingHub(ILogger<PhotoProcessingHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("Client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribe to updates for a specific job
    /// </summary>
    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"job:{jobId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to job {JobId}", Context.ConnectionId, jobId);
    }

    /// <summary>
    /// Unsubscribe from job updates
    /// </summary>
    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"job:{jobId}");
        _logger.LogDebug("Client {ConnectionId} unsubscribed from job {JobId}", Context.ConnectionId, jobId);
    }
}

/// <summary>
/// Photo processing progress event
/// </summary>
public class PhotoProgressEvent
{
    public required string JobId { get; init; }
    public required string PhotoId { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; } // "queued", "processing", "completed", "failed"
    public required string Stage { get; init; } // "upload", "thumbnails", "exif", "ai-description", "embedding"
    public string? Error { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Job completion event
/// </summary>
public class JobCompletionEvent
{
    public required string JobId { get; init; }
    public required string Status { get; init; } // "completed", "partial-success", "failed"
    public required int TotalPhotos { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public List<string> Errors { get; init; } = new();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
