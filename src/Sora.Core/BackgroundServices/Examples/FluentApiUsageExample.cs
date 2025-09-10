using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sora.Core.Observability.Health;

namespace Sora.Core.BackgroundServices.Examples;

/// <summary>
/// Example controller showing how to trigger background services using the fluent API
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class ExampleController : ControllerBase
{
    private readonly ILogger<ExampleController> _logger;

    public ExampleController(ILogger<ExampleController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Example: Upload a file and immediately trigger translation
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile([FromForm] string fileName, [FromQuery] string targetLanguage = "pt-BR")
    {
        // Simulate file upload
    var fileId = Guid.NewGuid().ToString("N");
        _logger.LogInformation("File uploaded: {FileId} -> {FileName}", fileId, fileName);

        // Trigger translation using the beautiful fluent API!
    await SoraServices.Do<TranslationService>("translate", new TranslationOptions(fileId, "auto", targetLanguage))
            .WithPriority(10)
            .WithTimeout(TimeSpan.FromMinutes(30))
            .ExecuteAsync();

        return Ok(new { FileId = fileId, Message = "File uploaded and translation started" });
    }

    /// <summary>
    /// Example: Manually trigger data cleanup
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> TriggerCleanup()
    {
    await SoraServices.Do<DataCleanupService>("check-queue")
            .ExecuteAsync();

        return Ok(new { Message = "Data cleanup triggered" });
    }

    /// <summary>
    /// Example: Get service status
    /// </summary>
    [HttpGet("services/{serviceName}/status")]
    public async Task<IActionResult> GetServiceStatus(string serviceName)
    {
        try
        {
            // Example for TranslationService
            if (serviceName.Equals("translation", StringComparison.OrdinalIgnoreCase))
            {
                var status = await SoraServices.Query<TranslationService>()
                    .GetStatusAsync();

                return Ok(status);
            }

            return NotFound(new { Error = "Service not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service status for {ServiceName}", serviceName);
            return StatusCode(500, new { Error = "Failed to retrieve service status" });
        }
    }
}

/// <summary>
/// Example background service that demonstrates complex workflow orchestration
/// </summary>
public class WorkflowOrchestrator : SoraFluentServiceBase
{
    public WorkflowOrchestrator(ILogger<WorkflowOrchestrator> logger, Microsoft.Extensions.Configuration.IConfiguration configuration)
        : base(logger, configuration) { }

    public override async Task ExecuteCoreAsync(CancellationToken cancellationToken)
    {
    Logger.LogInformation("Workflow orchestrator starting...");

        // Set up complex event-driven workflows using chainable subscriptions
    await SoraServices.On<TranslationService>(Sora.Core.Events.SoraServiceEvents.Translation.Started).Do<TranslationEventArgs>(async args =>
            {
                Logger.LogInformation("Workflow: Translation started for {FileId}", args.FileId);
                // Could trigger other services here
                await Task.CompletedTask;
            })
            .On(Sora.Core.Events.SoraServiceEvents.Translation.Completed).Do<TranslationEventArgs>(async args =>
            {
                Logger.LogInformation("Workflow: Translation completed for {FileId}, starting post-processing", args.FileId);
                
                // Chain to other services
                await SoraServices.Do<DataCleanupService>("check-queue")
                    .ExecuteAsync();
            })
            .On("TranslationFailed").Do<TranslationErrorArgs>(async args =>
            {
                Logger.LogError("Workflow: Translation failed for {FileId}: {Error}", args.FileId, args.Error);
                
                // Handle failures
                await HandleTranslationFailure(args);
            })
            .SubscribeAsync();

        // Keep orchestrator running
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private async Task HandleTranslationFailure(TranslationErrorArgs args)
    {
        // Implementation for handling translation failures
    Logger.LogInformation("Handling translation failure for {FileId}", args.FileId);
        await Task.Delay(1000); // Simulate failure handling
    }
}

/// <summary>
/// Example service that shows different periodic patterns
/// </summary>
[SoraPeriodicService(IntervalSeconds = 1800)] // 30 minutes
public class AdvancedPeriodicService : SoraPokablePeriodicServiceBase
{
    public override TimeSpan Period => TimeSpan.FromMinutes(30);

    public AdvancedPeriodicService(ILogger<AdvancedPeriodicService> logger, Microsoft.Extensions.Configuration.IConfiguration configuration)
        : base(logger, configuration) { }

    protected override async Task ExecutePeriodicAsync(CancellationToken cancellationToken)
    {
    Logger.LogInformation("Advanced periodic service executing...");
        
        // This runs every 30 minutes automatically
        await PerformRoutineMaintenance(cancellationToken);
    }

    protected override async Task OnTriggerNow(CancellationToken cancellationToken)
    {
    Logger.LogInformation("Advanced periodic service triggered on-demand");
        
        // This runs when someone calls the trigger API
        await PerformUrgentMaintenance(cancellationToken);
    }

    protected override async Task OnProcessBatch(int? batchSize, string? filter, CancellationToken cancellationToken)
    {
    Logger.LogInformation("Processing batch with size {BatchSize} and filter '{Filter}'", batchSize, filter);
        
        // This runs when someone calls the process-batch API
        await ProcessSpecificBatch(batchSize ?? 100, filter, cancellationToken);
    }

    private async Task PerformRoutineMaintenance(CancellationToken cancellationToken)
    {
        // Regular maintenance tasks
        await Task.Delay(2000, cancellationToken);
    Logger.LogInformation("Routine maintenance completed");
    }

    private async Task PerformUrgentMaintenance(CancellationToken cancellationToken)
    {
        // Urgent maintenance tasks
        await Task.Delay(1000, cancellationToken);
    Logger.LogInformation("Urgent maintenance completed");
    }

    private async Task ProcessSpecificBatch(int batchSize, string? filter, CancellationToken cancellationToken)
    {
        // Batch processing logic
        await Task.Delay(3000, cancellationToken);
    Logger.LogInformation("Processed batch of {BatchSize} items with filter '{Filter}'", batchSize, filter);
    }
}