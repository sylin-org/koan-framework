using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Koan.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.SelfOrchestration;

/// <summary>
/// Orchestrates dependencies for self-hosted Koan applications by discovering
/// required dependencies from loaded assemblies and managing their lifecycle
/// </summary>
public class KoanDependencyOrchestrator : IKoanDependencyOrchestrator
{
    private readonly IKoanContainerManager _containerManager;
    private readonly ILogger<KoanDependencyOrchestrator> _logger;
    private readonly IConfiguration _configuration;
    private readonly List<DependencyDescriptor> _managedDependencies = new();
    private readonly string _sessionId;
    private readonly string _appId;
    private readonly string _appInstance;
    private readonly Dictionary<string, string> _koanEnvironmentVariables;

    public KoanDependencyOrchestrator(
        IKoanContainerManager containerManager,
        ILogger<KoanDependencyOrchestrator> logger,
        IConfiguration configuration)
    {
        _containerManager = containerManager;
        _logger = logger;
        _configuration = configuration;

        // Generate app identity using KISS approach
        _appId = Assembly.GetEntryAssembly()?.GetName().Name ?? "UnknownApp";
        _appInstance = GenerateAppInstance(_appId);
        _sessionId = KoanEnv.SessionId;

        // Set environment variables for container labeling and other components
        Environment.SetEnvironmentVariable("KOAN_APP_ID", _appId);
        Environment.SetEnvironmentVariable("KOAN_APP_INSTANCE", _appInstance);
        Environment.SetEnvironmentVariable("KOAN_APP_SID", _sessionId);
        Environment.SetEnvironmentVariable("KOAN_SESSION_ID", _sessionId); // Maintain backward compatibility

        // Initialize cached Koan environment variables for container injection
        _koanEnvironmentVariables = new Dictionary<string, string>
        {
            ["KOAN_SESSION_ID"] = _sessionId,
            ["KOAN_APP_SID"] = _sessionId,
            ["KOAN_APP_ID"] = _appId,
            ["KOAN_APP_INSTANCE"] = _appInstance,
            ["KOAN_MANAGED_BY"] = "self-orchestration"
        };
    }

    public async Task StartDependenciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Self-orchestration starting dependency discovery for session {SessionId}", _sessionId);

        // Discover dependencies from loaded assemblies
        var dependencies = DiscoverRequiredDependencies();

        if (dependencies.Count == 0)
        {
            _logger.LogInformation("No dependencies discovered - continuing without orchestration");
            return;
        }

        _logger.LogInformation("Self-orchestration starting {Count} dependencies: {Dependencies}",
            dependencies.Count, string.Join(", ", dependencies.Select(d => d.Name)));

        // Start dependencies in priority order
        foreach (var dependency in dependencies.OrderBy(d => d.StartupPriority))
        {
            try
            {
                var containerName = await _containerManager.StartContainerAsync(dependency, _sessionId, cancellationToken);
                _managedDependencies.Add(dependency);

                _logger.LogDebug("Waiting for {DependencyName} to become healthy...", dependency.Name);
                var isHealthy = await _containerManager.WaitForContainerHealthyAsync(containerName, dependency, cancellationToken);

                if (!isHealthy)
                {
                    _logger.LogWarning("{DependencyName} did not become healthy within timeout", dependency.Name);
                }
                else
                {
                    _logger.LogInformation("{DependencyName} is healthy and ready", dependency.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start dependency {DependencyName}", dependency.Name);
                throw new InvalidOperationException($"Failed to start required dependency {dependency.Name}: {ex.Message}", ex);
            }
        }

        _logger.LogInformation("All dependencies started successfully - application ready to start");
    }

    public async Task StopDependenciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Self-orchestration stopping {Count} dependencies for session {SessionId}",
            _managedDependencies.Count, _sessionId);

        try
        {
            await _containerManager.CleanupSessionContainersAsync(_sessionId, cancellationToken);
            _managedDependencies.Clear();
            _logger.LogInformation("Self-orchestration cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop dependencies");
        }
    }

    public async Task<List<DependencyDescriptor>> GetManagedDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_managedDependencies.ToList());
    }

    private string GenerateAppInstance(string appId)
    {
        // Create unique instance ID: appId + path hash (KISS approach)
        var currentPath = Path.GetFileName(Environment.CurrentDirectory) ?? "unknown";
        var instanceInput = $"{appId}-{currentPath}";

        // Use SHA256 like other Koan components for consistency
        using var hasher = SHA256.Create();
        var hashBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(instanceInput));
        var hashHex = Convert.ToHexString(hashBytes);

        // Return app-id + first 8 chars of hash (keeps it readable but unique)
        return $"{appId}-{hashHex[..8].ToLowerInvariant()}";
    }

    private List<DependencyDescriptor> DiscoverRequiredDependencies()
    {
        var dependencies = new List<DependencyDescriptor>();
        var koanAssemblies = KoanEnv.KoanAssemblies;

        _logger.LogDebug("Checking {AssemblyCount} cached Koan assemblies for dependencies", koanAssemblies.Length);

        // Create a fast lookup for assembly names
        var assemblyNames = new HashSet<string>(
            koanAssemblies.Select(a => a.GetName().Name ?? ""),
            StringComparer.OrdinalIgnoreCase);

        // Scan for Koan data providers and AI services using KoanEnv assembly information
        if (assemblyNames.Contains("Koan.Data.Postgres"))
        {
            dependencies.Add(CreatePostgresDependency());
            _logger.LogDebug("Discovered Postgres dependency");
        }

        if (assemblyNames.Contains("Koan.Data.Redis"))
        {
            dependencies.Add(CreateRedisDependency());
            _logger.LogDebug("Discovered Redis dependency");
        }

        if (assemblyNames.Contains("Koan.AI.Ollama"))
        {
            dependencies.Add(CreateOllamaDependency());
            _logger.LogDebug("Discovered Ollama dependency");
        }

        if (assemblyNames.Contains("Koan.AI.Weaviate"))
        {
            dependencies.Add(CreateWeaviateDependency());
            _logger.LogDebug("Discovered Weaviate dependency");
        }

        return dependencies;
    }

    private Dictionary<string, string> CreateEnvironmentVariables(string dependencyType, Dictionary<string, string>? serviceSpecific = null)
    {
        var environment = new Dictionary<string, string>(_koanEnvironmentVariables)
        {
            ["KOAN_DEPENDENCY_TYPE"] = dependencyType
        };

        if (serviceSpecific != null)
        {
            foreach (var kvp in serviceSpecific)
            {
                environment[kvp.Key] = kvp.Value;
            }
        }

        return environment;
    }

    private DependencyDescriptor CreatePostgresDependency()
    {
        // Read configuration using same patterns as PostgresOptionsConfigurator
        var databaseName = Configuration.ReadFirst(_configuration, "KoanAspireDemo",
            "Koan:Data:Postgres:Database",
            "Koan:Data:Database",
            "ConnectionStrings:Database");

        // Try to extract credentials from existing connection strings
        var username = "postgres";  // Default
        var password = "postgres";  // Default

        // Check for explicit username/password configuration
        var configuredUsername = Configuration.ReadFirst(_configuration, "",
            "Koan:Data:Postgres:Username",
            "Koan:Data:Username");
        if (!string.IsNullOrWhiteSpace(configuredUsername))
        {
            username = configuredUsername;
        }

        var configuredPassword = Configuration.ReadFirst(_configuration, "",
            "Koan:Data:Postgres:Password",
            "Koan:Data:Password");
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            password = configuredPassword;
        }

        return new DependencyDescriptor
        {
            Name = "postgres",
            Image = "postgres:17.0",
            Port = 5432,
            StartupPriority = 100,
            HealthTimeout = TimeSpan.FromSeconds(30),
            HealthCheckCommand = "pg_isready -h localhost -p 5432",
            Environment = CreateEnvironmentVariables("postgres", new Dictionary<string, string>
            {
                ["POSTGRES_DB"] = databaseName,
                ["POSTGRES_USER"] = username,
                ["POSTGRES_PASSWORD"] = password
            }),
            Volumes = new List<string>
            {
                $"koan-postgres-{_sessionId}:/var/lib/postgresql/data"
            }
        };
    }

    private DependencyDescriptor CreateRedisDependency()
    {
        return new DependencyDescriptor
        {
            Name = "redis",
            Image = "redis:7.4",
            Port = 6379,
            StartupPriority = 200,
            HealthTimeout = TimeSpan.FromSeconds(15),
            HealthCheckCommand = "redis-cli ping",
            Environment = CreateEnvironmentVariables("redis"),
            Volumes = new List<string>
            {
                $"koan-redis-{_sessionId}:/data"
            }
        };
    }

    private DependencyDescriptor CreateOllamaDependency()
    {
        return new DependencyDescriptor
        {
            Name = "ollama",
            Image = "ollama/ollama:latest",
            Port = 11434,
            StartupPriority = 500,
            HealthTimeout = TimeSpan.FromSeconds(60),
            Environment = CreateEnvironmentVariables("ollama"),
            Volumes = new List<string>
            {
                $"koan-ollama-{_sessionId}:/root/.ollama"
            }
        };
    }

    private DependencyDescriptor CreateWeaviateDependency()
    {
        return new DependencyDescriptor
        {
            Name = "weaviate",
            Image = "semitechnologies/weaviate:latest",
            Port = 8080,
            StartupPriority = 300,
            HealthTimeout = TimeSpan.FromSeconds(45),
            Environment = CreateEnvironmentVariables("weaviate", new Dictionary<string, string>
            {
                ["QUERY_DEFAULTS_LIMIT"] = "25",
                ["AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED"] = "true",
                ["PERSISTENCE_DATA_PATH"] = "/var/lib/weaviate"
            }),
            Volumes = new List<string>
            {
                $"koan-weaviate-{_sessionId}:/var/lib/weaviate"
            }
        };
    }
}