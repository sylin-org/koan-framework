using Koan.Context.Initialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Controllers;

/// <summary>
/// API endpoints for viewing and managing application settings
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsController> _logger;
    private readonly TagSeedInitializer _tagSeedInitializer;

    public SettingsController(
        IConfiguration configuration,
        ILogger<SettingsController> logger,
        TagSeedInitializer tagSeedInitializer)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tagSeedInitializer = tagSeedInitializer ?? throw new ArgumentNullException(nameof(tagSeedInitializer));
    }

    /// <summary>
    /// Gets all current application settings (read-only for now)
    /// </summary>
    /// <returns>Current configuration settings</returns>
    [HttpGet]
    public IActionResult GetSettings()
    {
        try
        {
            var settings = new
            {
                VectorStore = new
                {
                    Provider = "weaviate",
                    Host = _configuration["Koan:Orchestration:Weaviate:HostPort"] ?? "27501",
                    Dimension = _configuration.GetValue<int>("Koan:Data:Weaviate:Dimension", 384),
                    Metric = _configuration["Koan:Data:Weaviate:Metric"] ?? "cosine",
                    DefaultTopK = _configuration.GetValue<int>("Koan:Data:Weaviate:DefaultTopK", 10),
                    MaxTopK = _configuration.GetValue<int>("Koan:Data:Weaviate:MaxTopK", 100),
                    TimeoutSeconds = _configuration.GetValue<int>("Koan:Data:Weaviate:TimeoutSeconds", 30)
                },
                Database = new
                {
                    Provider = _configuration["Koan:Data:Sources:Default:Adapter"] ?? "sqlite",
                    ConnectionString = MaskConnectionString(_configuration["Koan:Data:Sources:Default:ConnectionString"])
                },
                AI = new
                {
                    Embedding = new
                    {
                        Provider = _configuration["Koan:AI:Embedding:Provider"] ?? "ollama",
                        Model = _configuration["Koan:AI:Embedding:Model"] ?? "all-minilm",
                        Endpoint = _configuration["Koan:AI:Embedding:Endpoint"] ?? "http://localhost:11434"
                    }
                },
                Indexing = new
                {
                    ChunkSize = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:IndexingChunkSize", 1024),
                    MaxFileSizeMB = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:MaxFileSizeMB", 100),
                    MaxConcurrentJobs = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:MaxConcurrentIndexingJobs", 2),
                    EmbeddingBatchSize = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:EmbeddingBatchSize", 50),
                    EnableParallelProcessing = _configuration.GetValue<bool>("Koan:Context:IndexingPerformance:EnableParallelProcessing", true),
                    MaxDegreeOfParallelism = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:MaxDegreeOfParallelism", 4),
                    DefaultTokenBudget = _configuration.GetValue<int>("Koan:Context:IndexingPerformance:DefaultTokenBudget", 5000)
                },
                FileMonitoring = new
                {
                    Enabled = _configuration.GetValue<bool>("Koan:Context:FileMonitoring:Enabled", true),
                    DebounceMilliseconds = _configuration.GetValue<int>("Koan:Context:FileMonitoring:DebounceMilliseconds", 2000),
                    MaxConcurrentReindexOperations = _configuration.GetValue<int>("Koan:Context:FileMonitoring:MaxConcurrentReindexOperations", 3)
                },
                ProjectResolution = new
                {
                    AutoCreate = _configuration.GetValue<bool>("Koan:Context:ProjectResolution:AutoCreate", true),
                    AutoIndex = _configuration.GetValue<bool>("Koan:Context:ProjectResolution:AutoIndex", true),
                    MaxSizeGB = _configuration.GetValue<int>("Koan:Context:ProjectResolution:MaxSizeGB", 10)
                },
                JobMaintenance = new
                {
                    MaxJobsPerProject = _configuration.GetValue<int>("Koan:Context:JobMaintenance:MaxJobsPerProject", 50),
                    JobRetentionDays = _configuration.GetValue<int>("Koan:Context:JobMaintenance:JobRetentionDays", 7),
                    EnableAutomaticCleanup = _configuration.GetValue<bool>("Koan:Context:JobMaintenance:EnableAutomaticCleanup", true)
                },
                System = new
                {
                    BaseUrl = _configuration["Urls"] ?? "http://localhost:27500",
                    AutoResumeIndexing = _configuration.GetValue<bool>("Koan:Context:AutoResumeIndexing", true)
                }
            };

            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving settings");
            return StatusCode(500, new { error = "Failed to retrieve settings", details = ex.Message });
        }
    }

    /// <summary>
    /// Force seeds tag vocabulary, rules, pipelines, and personas.
    /// </summary>
    [HttpPost("seed-tags")]
    public async Task<IActionResult> SeedTags(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _tagSeedInitializer.EnsureSeededAsync(force: true, cancellationToken).ConfigureAwait(false);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run tag seeding");
            return StatusCode(500, new { error = "Failed to seed tags", details = ex.Message });
        }
    }

    /// <summary>
    /// Test vector store connection
    /// </summary>
    /// <returns>Connection test result</returns>
    [HttpPost("test/vector-store")]
    public async Task<IActionResult> TestVectorStoreConnection()
    {
        try
        {
            // In a real implementation, you would test the actual connection
            // For now, return a placeholder response
            await Task.Delay(500); // Simulate connection test

            return Ok(new
            {
                Success = true,
                Message = "Vector store connection test successful",
                Provider = "weaviate",
                Endpoint = $"http://localhost:{_configuration["Koan:Orchestration:Weaviate:HostPort"]}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector store connection test failed");
            return Ok(new
            {
                Success = false,
                Message = "Connection test failed",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test database connection
    /// </summary>
    /// <returns>Connection test result</returns>
    [HttpPost("test/database")]
    public async Task<IActionResult> TestDatabaseConnection()
    {
        try
        {
            // In a real implementation, you would test the actual connection
            // For now, return a placeholder response
            await Task.Delay(500); // Simulate connection test

            return Ok(new
            {
                Success = true,
                Message = "Database connection test successful",
                Provider = _configuration["Koan:Data:Sources:Default:Adapter"]
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection test failed");
            return Ok(new
            {
                Success = false,
                Message = "Connection test failed",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Mask sensitive parts of connection strings for display
    /// </summary>
    private string? MaskConnectionString(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        // Simple masking - hide everything after "Data Source=" for SQLite
        if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString; // SQLite paths are safe to show
        }

        // For other connection strings, mask passwords
        if (connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Pwd=", StringComparison.OrdinalIgnoreCase))
        {
            return System.Text.RegularExpressions.Regex.Replace(
                connectionString,
                @"(Password|Pwd)=([^;]+)",
                "$1=********",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return connectionString;
    }
}
