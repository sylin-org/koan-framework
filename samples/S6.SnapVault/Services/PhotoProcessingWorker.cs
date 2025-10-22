namespace S6.SnapVault.Services;

/// <summary>
/// Background service that processes queued photo uploads
/// Runs continuously and processes one photo at a time to avoid overwhelming the system
/// </summary>
public class PhotoProcessingWorker : BackgroundService
{
    private readonly ILogger<PhotoProcessingWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPhotoProcessingQueue _queue;

    public PhotoProcessingWorker(
        ILogger<PhotoProcessingWorker> logger,
        IServiceScopeFactory scopeFactory,
        IPhotoProcessingQueue queue)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _queue = queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Photo processing worker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait for items to be available
                await _queue.WaitForItemsAsync(stoppingToken);

                // Process all available items (but not blocking - process one at a time)
                while (_queue.TryDequeue(out var queuedUpload))
                {
                    await ProcessQueuedPhotoAsync(queuedUpload, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in photo processing worker loop");
                // Continue processing - don't let one error stop the worker
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Photo processing worker stopped");
    }

    private async Task ProcessQueuedPhotoAsync(QueuedPhotoUpload queuedUpload, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Processing queued photo: {FileName} for job {JobId}",
            queuedUpload.FileName, queuedUpload.JobId);

        // Create a new scope for this processing operation
        using var scope = _scopeFactory.CreateScope();
        var processingService = scope.ServiceProvider.GetRequiredService<IPhotoProcessingService>();

        try
        {
            // Convert byte array back to stream
            using var fileStream = new MemoryStream(queuedUpload.FileData);

            // Create a fake IFormFile from the cached data
            var formFile = new FormFileWrapper(
                fileStream,
                queuedUpload.FileName,
                queuedUpload.ContentType);

            // Process the upload (this will handle thumbnails, EXIF, and background AI)
            // Use CancellationToken.None to ensure processing completes even if service is stopping
            await processingService.ProcessUploadAsync(
                queuedUpload.EventId,
                formFile,
                queuedUpload.JobId,
                CancellationToken.None);

            _logger.LogInformation("Successfully processed queued photo: {FileName} for job {JobId}",
                queuedUpload.FileName, queuedUpload.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process queued photo: {FileName} for job {JobId}",
                queuedUpload.FileName, queuedUpload.JobId);

            // Note: Error handling and job status updates will be done by the processing service
        }
    }
}

/// <summary>
/// Simple wrapper to convert cached byte array back to IFormFile
/// </summary>
internal class FormFileWrapper : IFormFile
{
    private readonly Stream _stream;
    private readonly string _fileName;
    private readonly string _contentType;

    public FormFileWrapper(Stream stream, string fileName, string contentType)
    {
        _stream = stream;
        _fileName = fileName;
        _contentType = contentType;
    }

    public string ContentType => _contentType;
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{_fileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _stream.Length;
    public string Name => "file";
    public string FileName => _fileName;

    public Stream OpenReadStream() => _stream;

    public void CopyTo(Stream target) => _stream.CopyTo(target);

    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => _stream.CopyToAsync(target, cancellationToken);
}
