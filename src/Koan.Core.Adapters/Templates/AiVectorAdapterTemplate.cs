using Koan.Orchestration.Models;

namespace Koan.Core.Adapters.Templates;

/// <summary>
/// Template for generating AI/Vector service adapter scaffolding.
/// Provides patterns for embeddings, vector storage, and AI model integration.
/// </summary>
public class AiVectorAdapterTemplate : IAdapterTemplate
{
    public string Name => "AI/Vector Adapter";
    public string Description => "Generates an AI/Vector service adapter with embeddings, chat, and vector storage capabilities";
    public ServiceType ServiceType => ServiceType.Ai;

    public string GenerateCode(AdapterTemplateParameters parameters)
    {
        var usingStatements = string.Join(Environment.NewLine,
            new[] { "using Microsoft.Extensions.Configuration;", "using Microsoft.Extensions.Logging;", "using Koan.Core.Adapters;", "using Koan.Orchestration.Models;" }
            .Concat(parameters.UsingStatements)
            .Select(u => u.EndsWith(";") ? u : u + ";"));

        return $@"{usingStatements}

namespace {parameters.Namespace};

/// <summary>
/// {parameters.DisplayName} adapter implementation
/// </summary>
public class {parameters.ClassName} : BaseKoanAdapter
{{
    public override ServiceType ServiceType => ServiceType.{parameters.ServiceType};
    public override string AdapterId => ""{parameters.AdapterId}"";
    public override string DisplayName => ""{parameters.DisplayName}"";

    public override AdapterCapabilities Capabilities => AdapterCapabilities.Create()
        .WithHealth(HealthCapabilities.Basic | HealthCapabilities.ConnectionHealth | HealthCapabilities.ResponseTime)
        .WithConfiguration(ConfigurationCapabilities.EnvironmentVariables | ConfigurationCapabilities.ConfigurationFiles | ConfigurationCapabilities.OrchestrationAware)
        .WithSecurity(SecurityCapabilities.None) // Configure based on AI service requirements
        .WithData(ExtendedQueryCapabilities.VectorSearch | ExtendedQueryCapabilities.SemanticSearch | ExtendedQueryCapabilities.Embeddings)
        .WithCustom(""chat"", ""streaming"", ""embeddings"");

    private readonly HttpClient _httpClient;

    public {parameters.ClassName}(HttpClient httpClient, ILogger<{parameters.ClassName}> logger, IConfiguration configuration)
        : base(logger, configuration)
    {{
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }}

    protected override async Task InitializeAdapterAsync(CancellationToken cancellationToken = default)
    {{
        var baseUrl = GetConnectionString();
        if (string.IsNullOrEmpty(baseUrl))
        {{
            throw new InvalidOperationException(""Base URL not configured for {parameters.DisplayName}"");
        }}

        if (_httpClient.BaseAddress == null)
        {{
            _httpClient.BaseAddress = new Uri(baseUrl);
        }}

        Logger.LogInformation(""[{{AdapterId}}] Initializing {parameters.DisplayName} connection"", AdapterId);

        // TODO: Test connectivity
        // Example: await TestConnectivityAsync(cancellationToken);

        Logger.LogInformation(""[{{AdapterId}}] {parameters.DisplayName} connection established"", AdapterId);
    }}

    protected override async Task<IReadOnlyDictionary<string, object?>?> CheckAdapterHealthAsync(CancellationToken cancellationToken = default)
    {{
        try
        {{
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // TODO: Implement health check
            // Example: using var response = await _httpClient.GetAsync(""/health"", cancellationToken);
            // response.EnsureSuccessStatusCode();

            stopwatch.Stop();

            var healthData = new Dictionary<string, object?>
            {{
                [""status""] = ""healthy"",
                [""response_time_ms""] = stopwatch.ElapsedMilliseconds,
                [""base_url""] = _httpClient.BaseAddress?.ToString(),
                [""provider""] = ""{parameters.DisplayName}""
            }};

            // TODO: Add service-specific health metrics
            // Example:
            // try
            // {{
            //     var models = await ListModelsAsync(cancellationToken);
            //     healthData[""available_models""] = models.Count;
            // }}
            // catch (Exception ex)
            // {{
            //     healthData[""models_error""] = ex.Message;
            // }}

            return healthData;
        }}
        catch (Exception ex)
        {{
            Logger.LogWarning(ex, ""[{{AdapterId}}] Health check failed"", AdapterId);
            return new Dictionary<string, object?>
            {{
                [""status""] = ""unhealthy"",
                [""error""] = ex.Message
            }};
        }}
    }}

    protected override Task<IReadOnlyDictionary<string, object?>?> GetAdapterBootstrapMetadataAsync(CancellationToken cancellationToken = default)
    {{
        var metadata = new Dictionary<string, object?>
        {{
            [""base_url""] = _httpClient.BaseAddress?.ToString(),
            [""provider""] = ""{parameters.DisplayName}"",
            [""adapter_type""] = ""ai_vector"",
            [""features""] = new[] {{ ""chat"", ""embeddings"", ""vector_search"" }},
            [""capabilities""] = Capabilities.GetCapabilitySummary()
        }};

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(metadata);
    }}

    // TODO: Add AI/Vector specific methods
    // Example:
    // public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
    // {{
    //     // Implementation for text generation
    // }}
    //
    // public async Task<float[]> GetEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
    // {{
    //     // Implementation for embeddings
    // }}
}}";
    }

    public AdapterTemplateDefinition GetTemplateDefinition()
    {
        return new AdapterTemplateDefinition
        {
            Name = Name,
            Description = Description,
            ServiceType = ServiceType,
            RequiredParameters = new List<string> { "ClassName", "AdapterId", "DisplayName", "Namespace" },
            OptionalParameters = new Dictionary<string, object>
            {
                { "IsCritical", true },
                { "Priority", 100 }
            },
            ParameterDescriptions = new Dictionary<string, string>
            {
                { "ClassName", "The class name for the adapter" },
                { "AdapterId", "Unique identifier for adapter registration" },
                { "DisplayName", "Human-readable display name" },
                { "Namespace", "Namespace for the generated class" }
            },
            ParameterExamples = new Dictionary<string, string>
            {
                { "ClassName", "OllamaAdapter" },
                { "AdapterId", "ollama" },
                { "DisplayName", "Ollama AI Provider" },
                { "Namespace", "MyApp.AI.Ollama" }
            }
        };
    }
}