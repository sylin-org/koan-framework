using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// API endpoints for managing Koan.Context configuration
/// </summary>
[ApiController]
[Route("api/configuration")]
public class ConfigurationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;
    private static readonly object _lock = new();
    private static KoanContextSettings? _cachedSettings;

    public ConfigurationController(
        IConfiguration configuration,
        ILogger<ConfigurationController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets current configuration settings
    /// </summary>
    [HttpGet]
    public IActionResult GetConfiguration()
    {
        lock (_lock)
        {
            if (_cachedSettings == null)
            {
                _cachedSettings = LoadSettings();
            }

            return Ok(_cachedSettings);
        }
    }

    /// <summary>
    /// Updates configuration settings
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateConfiguration([FromBody] KoanContextSettings settings)
    {
        try
        {
            // Validate settings
            if (settings.AutoResumeDelay < 0)
            {
                return BadRequest(new { error = "AutoResumeDelay must be non-negative" });
            }

            // Update cached settings
            lock (_lock)
            {
                _cachedSettings = settings;
            }

            // Write to settings.local.json (user-specific, gitignored)
            var settingsPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "settings.local.json");

            var json = System.Text.Json.JsonSerializer.Serialize(
                new { Koan = new { Context = settings } },
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

            await System.IO.File.WriteAllTextAsync(settingsPath, json);

            _logger.LogInformation(
                "Configuration updated: AutoResumeDelay={Delay}s, AutoResumeEnabled={Enabled}",
                settings.AutoResumeDelay,
                settings.AutoResumeIndexing);

            return Ok(new
            {
                message = "Configuration updated successfully. Restart the service for changes to take effect.",
                settings = _cachedSettings,
                requiresRestart = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update configuration");
            return StatusCode(500, new { error = "Failed to update configuration", message = ex.Message });
        }
    }

    private KoanContextSettings LoadSettings()
    {
        var section = _configuration.GetSection("Koan:Context");

        return new KoanContextSettings
        {
            AutoResumeIndexing = section.GetValue("AutoResumeIndexing", true),
            AutoResumeDelay = section.GetValue("AutoResumeDelay", 0),

            JobMaintenance = new JobMaintenanceSettings
            {
                MaxJobsPerProject = section.GetValue("JobMaintenance:MaxJobsPerProject", 50),
                JobRetentionDays = section.GetValue("JobMaintenance:JobRetentionDays", 7),
                EnableAutomaticCleanup = section.GetValue("JobMaintenance:EnableAutomaticCleanup", true)
            },

            IndexingPerformance = new IndexingPerformanceSettings
            {
                MaxConcurrentIndexingJobs = section.GetValue("IndexingPerformance:MaxConcurrentIndexingJobs", 2),
                EmbeddingBatchSize = section.GetValue("IndexingPerformance:EmbeddingBatchSize", 50),
                DefaultTokenBudget = section.GetValue("IndexingPerformance:DefaultTokenBudget", 5000),
                IndexingChunkSize = section.GetValue("IndexingPerformance:IndexingChunkSize", 1024),
                MaxFileSizeMB = section.GetValue("IndexingPerformance:MaxFileSizeMB", 10),
                EnableParallelProcessing = section.GetValue("IndexingPerformance:EnableParallelProcessing", true),
                MaxDegreeOfParallelism = section.GetValue("IndexingPerformance:MaxDegreeOfParallelism", 4)
            },

            StorageCleanup = new StorageCleanupSettings
            {
                EnableAutomaticChunkCleanup = section.GetValue("StorageCleanup:EnableAutomaticChunkCleanup", true),
                VacuumDatabaseOnStartup = section.GetValue("StorageCleanup:VacuumDatabaseOnStartup", false),
                MaxDatabaseSizeGB = section.GetValue("StorageCleanup:MaxDatabaseSizeGB", 50),
                EnableEmbeddingCompression = section.GetValue("StorageCleanup:EnableEmbeddingCompression", false),
                EmbeddingCompressionDays = section.GetValue("StorageCleanup:EmbeddingCompressionDays", 30)
            },

            LoggingDiagnostics = new LoggingDiagnosticsSettings
            {
                EnableDetailedIndexingLogs = section.GetValue("LoggingDiagnostics:EnableDetailedIndexingLogs", false),
                LogRetentionDays = section.GetValue("LoggingDiagnostics:LogRetentionDays", 30),
                EnablePerformanceMetrics = section.GetValue("LoggingDiagnostics:EnablePerformanceMetrics", true),
                EnableQueryTracing = section.GetValue("LoggingDiagnostics:EnableQueryTracing", false),
                SlowQueryThresholdMs = section.GetValue("LoggingDiagnostics:SlowQueryThresholdMs", 1000)
            },

            ProjectResolution = new ProjectResolutionSettings
            {
                FollowSymbolicLinks = section.GetValue("ProjectResolution:FollowSymbolicLinks", true),
                MatchAncestorProjects = section.GetValue("ProjectResolution:MatchAncestorProjects", true),
                MaxAncestorDepth = section.GetValue("ProjectResolution:MaxAncestorDepth", 2),
                AutoCreateProjectOnQuery = section.GetValue("ProjectResolution:AutoCreateProjectOnQuery", true),
                AutoIndexInBackground = section.GetValue("ProjectResolution:AutoIndexInBackground", true),
                AutoIndexRequireGitRepository = section.GetValue("ProjectResolution:AutoIndexRequireGitRepository", false),
                AutoIndexMaxSizeGB = section.GetValue("ProjectResolution:AutoIndexMaxSizeGB", 10),
                TopKMode = section.GetValue("ProjectResolution:TopKMode", "per-project")
            },

            FileMonitoring = new FileMonitoringSettings
            {
                Enabled = section.GetValue("FileMonitoring:Enabled", true),
                DebounceMilliseconds = section.GetValue("FileMonitoring:DebounceMilliseconds", 2000),
                BatchWindowMilliseconds = section.GetValue("FileMonitoring:BatchWindowMilliseconds", 5000),
                MaxConcurrentReindexOperations = section.GetValue("FileMonitoring:MaxConcurrentReindexOperations", 3),
                MaxConcurrentFileWatchers = section.GetValue("FileMonitoring:MaxConcurrentFileWatchers", 10),
                RestartOnFailure = section.GetValue("FileMonitoring:RestartOnFailure", true),
                RestartDelayMilliseconds = section.GetValue("FileMonitoring:RestartDelayMilliseconds", 5000)
            }
        };
    }
}

/// <summary>
/// Koan.Context configuration settings
/// </summary>
public record KoanContextSettings
{
    /// <summary>
    /// Enable automatic resume of interrupted indexing jobs on startup
    /// </summary>
    public bool AutoResumeIndexing { get; init; } = true;

    /// <summary>
    /// Delay in seconds before auto-resuming jobs (0 = immediate, -1 = never)
    /// Useful to prevent immediate resource usage on startup
    /// </summary>
    public int AutoResumeDelay { get; init; } = 0;

    public JobMaintenanceSettings JobMaintenance { get; init; } = new();
    public IndexingPerformanceSettings IndexingPerformance { get; init; } = new();
    public StorageCleanupSettings StorageCleanup { get; init; } = new();
    public LoggingDiagnosticsSettings LoggingDiagnostics { get; init; } = new();
    public ProjectResolutionSettings ProjectResolution { get; init; } = new();
    public FileMonitoringSettings FileMonitoring { get; init; } = new();
}

/// <summary>
/// Job maintenance and cleanup settings
/// </summary>
public record JobMaintenanceSettings
{
    /// <summary>
    /// Maximum number of jobs to retain per project for audit history
    /// </summary>
    public int MaxJobsPerProject { get; init; } = 50;

    /// <summary>
    /// How many days to keep completed/failed/cancelled jobs before deletion
    /// </summary>
    public int JobRetentionDays { get; init; } = 7;

    /// <summary>
    /// Enable automatic cleanup of orphaned jobs on startup
    /// </summary>
    public bool EnableAutomaticCleanup { get; init; } = true;
}

/// <summary>
/// Indexing performance tuning settings
/// </summary>
public record IndexingPerformanceSettings
{
    /// <summary>
    /// Maximum number of projects that can be indexed concurrently
    /// </summary>
    public int MaxConcurrentIndexingJobs { get; init; } = 2;

    /// <summary>
    /// Number of chunks to process in a single embedding batch
    /// </summary>
    public int EmbeddingBatchSize { get; init; } = 50;

    /// <summary>
    /// Default token budget for search queries
    /// </summary>
    public int DefaultTokenBudget { get; init; } = 5000;

    /// <summary>
    /// Target chunk size in tokens for code splitting
    /// </summary>
    public int IndexingChunkSize { get; init; } = 1024;

    /// <summary>
    /// Maximum file size in MB to index (larger files are skipped)
    /// </summary>
    public int MaxFileSizeMB { get; init; } = 10;

    /// <summary>
    /// Enable parallel file processing during indexing
    /// </summary>
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Maximum degree of parallelism for file processing
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = 4;
}

/// <summary>
/// Storage and cleanup settings
/// </summary>
public record StorageCleanupSettings
{
    /// <summary>
    /// Automatically delete old chunks when reindexing a project
    /// </summary>
    public bool EnableAutomaticChunkCleanup { get; init; } = true;

    /// <summary>
    /// Run SQLite VACUUM on startup to reclaim space
    /// </summary>
    public bool VacuumDatabaseOnStartup { get; init; } = false;

    /// <summary>
    /// Alert threshold for database size in GB
    /// </summary>
    public int MaxDatabaseSizeGB { get; init; } = 50;

    /// <summary>
    /// Enable automatic compression of old vector embeddings
    /// </summary>
    public bool EnableEmbeddingCompression { get; init; } = false;

    /// <summary>
    /// Days before compressing embeddings (0 = never)
    /// </summary>
    public int EmbeddingCompressionDays { get; init; } = 30;
}

/// <summary>
/// Logging and diagnostics settings
/// </summary>
public record LoggingDiagnosticsSettings
{
    /// <summary>
    /// Enable detailed indexing progress logs
    /// </summary>
    public bool EnableDetailedIndexingLogs { get; init; } = false;

    /// <summary>
    /// Log retention in days (0 = keep all)
    /// </summary>
    public int LogRetentionDays { get; init; } = 30;

    /// <summary>
    /// Track and report indexing performance metrics
    /// </summary>
    public bool EnablePerformanceMetrics { get; init; } = true;

    /// <summary>
    /// Enable query performance tracing
    /// </summary>
    public bool EnableQueryTracing { get; init; } = false;

    /// <summary>
    /// Log slow queries that exceed threshold in milliseconds
    /// </summary>
    public int SlowQueryThresholdMs { get; init; } = 1000;
}

public record ProjectResolutionSettings
{
    public bool FollowSymbolicLinks { get; init; } = true;
    public bool MatchAncestorProjects { get; init; } = true;
    public int MaxAncestorDepth { get; init; } = 2;
    public bool AutoCreateProjectOnQuery { get; init; } = true;
    public bool AutoIndexInBackground { get; init; } = true;
    public bool AutoIndexRequireGitRepository { get; init; } = false;
    public int AutoIndexMaxSizeGB { get; init; } = 10;
    public string TopKMode { get; init; } = "per-project";
}

public record FileMonitoringSettings
{
    public bool Enabled { get; init; } = true;
    public int DebounceMilliseconds { get; init; } = 2000;
    public int BatchWindowMilliseconds { get; init; } = 5000;
    public int MaxConcurrentReindexOperations { get; init; } = 3;
    public int MaxConcurrentFileWatchers { get; init; } = 10;
    public bool RestartOnFailure { get; init; } = true;
    public int RestartDelayMilliseconds { get; init; } = 5000;
}
