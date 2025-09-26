## **Infrastructure & Bootstrap Refactoring Plan**

### 1. Baseline Stack (Docker Compose)

- Reuse the existing `docker compose up` workflow that launches API, MongoDB, Weaviate, and Ollama containers.
- Document environment variables in `.env.sample`:
  - `DOCMIND__Storage__Bucket=local`
  - `DOCMIND__Processing__MaxDegreeOfParallelism=2`
  - `DOCMIND__Ai__DefaultModel=llama3`
  - `DOCMIND__Ai__VisionModel=llava`
  - `DOCMIND__Embedding__Provider=weaviate`
- Provide optional override files (`compose.weaviate-disabled.yml`) for running without embeddings.

### 2. Program.cs Layout

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKoan(options =>
    {
        options.EnableMcp = true;
        options.McpTransports = McpTransports.Stdio | McpTransports.HttpSse;
    })
    .AddDocMind(); // extension provided by the registrar below

builder.Services.AddControllers();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
app.UseKoanMcp();
app.Run();
```

`AddDocMind()` is the new extension method that wires up all domain services, hosted workers, and options.

### 3. Auto-Registrar Structure

```csharp
public static class DocMindServiceCollectionExtensions
{
    public static IServiceCollection AddDocMind(this IServiceCollection services)
    {
        services.AddSingleton(Channel.CreateBounded<DocumentWorkItem>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        }));

        services.AddScoped<DocumentIntakeService>();
        services.AddScoped<TextExtractionService>();
        services.AddScoped<VisionInsightService>();
        services.AddScoped<InsightSynthesisService>();
        services.AddScoped<TemplateSuggestionService>();
        services.AddScoped<InsightAggregationService>();
        services.AddScoped<TemplateGeneratorService>();

        services.AddHostedService<DocumentAnalysisPipeline>();

        services.AddOptions<DocumentProcessingOptions>()
            .BindConfiguration("DocMind:Processing")
            .ValidateDataAnnotations();

        services.AddOptions<StorageOptions>()
            .BindConfiguration("DocMind:Storage");

        services.AddSingleton<IConfigureOptions<AiOptions>, ConfigureDocMindAiOptions>();

        return services;
    }
}
```

### 4. Configuration Model

| Section | Purpose | Sample Keys |
|---------|---------|-------------|
| `DocMind:Processing` | Queue limits, retry policy, concurrency | `QueueLimit`, `MaxDegreeOfParallelism`, `MaxRetries`, `BackoffSeconds` |
| `DocMind:Storage` | Storage provider selection | `Kind=FileSystem`, `Bucket`, `RootPath` |
| `DocMind:Vision` | Toggle vision processing | `Enabled`, `Model`, `MaxImagePixels` |
| `DocMind:Embedding` | Embedding provider configuration | `Provider=Weaviate`, `Endpoint`, `ApiKey` |

Use Koan’s capability detection to disable features when providers are unavailable (e.g., skip embeddings when Weaviate container is stopped).

### 5. Observability & Telemetry

- Enable Koan boot report and extend it with DocMind-specific sections (documenting queue configuration, provider readiness, sample documents).
- Add health checks:
  - `/health/storage` – verifies storage root writable.
  - `/health/embedding` – pings Weaviate when enabled.
  - `/health/models` – checks required Ollama models installed.
- Integrate OpenTelemetry exporters already supported by Koan; provide `otel-collector` compose override for workshops.

### 6. Deployment Profiles

- **Local (default)**: Compose stack with volume mounts for storage and Ollama models.
- **Lightweight demo**: Compose override disabling Weaviate + Vision; pipeline automatically skips embedding stage.
- **CI**: Use MongoDB memory server and stub AI providers (Koan AI test doubles) to run automated tests without external services.
- **Cloud**: Document how to point to managed MongoDB/Weaviate/Ollama endpoints via environment variables, keeping the same code path.

### 7. Developer Experience Enhancements

- Provide `scripts/docmind-reset.sh` to purge Mongo collections, storage folder, and Weaviate classes for clean demos.
- Include `launchSettings.json` profiles for API + Angular concurrently with pre-configured compose dependencies.
- Add sample `appsettings.Development.json` showing minimal configuration required to run the stack.

This infrastructure plan keeps the runtime stack approachable while exposing configuration hooks and diagnostics expected from a flagship Koan sample.
