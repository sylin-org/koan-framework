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
using Koan.Data.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

var app = builder.Build();

app.Run();
```

All DocMind services, controllers, MCP endpoints, and configuration are auto-discovered via `DocMindRegistrar` - no manual registration required. The framework automatically:

- **Discovers DocMindRegistrar**: `AddKoan()` scans assemblies and finds `DocMindRegistrar : IKoanAutoRegistrar`
- **Registers Services**: Background workers, AI services, storage providers, and options validation
- **Configures Web Pipeline**: Controllers, routing, authentication, and MCP endpoints via `KoanWebStartupFilter`
- **Handles Data Adapters**: MongoDB, Weaviate, and vector capabilities based on available providers

### 3. DocMindRegistrar Implementation

```csharp
// /Infrastructure/DocMindRegistrar.cs
public sealed class DocMindRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "S13.DocMind";
    public string? ModuleVersion => typeof(DocMindRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Configuration with validation
        services.AddKoanOptions<DocMindOptions>(DocMindOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DocMindOptions>, DocMindOptionsValidator>();

        // Core processing services
        services.AddScoped<IDocumentIntakeService, DocumentIntakeService>();
        services.AddScoped<ITextExtractionService, TextExtractionService>();
        services.AddScoped<IVisionInsightService, VisionInsightService>();
        services.AddScoped<IInsightSynthesisService, InsightSynthesisService>();
        services.AddScoped<ITemplateSuggestionService, TemplateSuggestionService>();

        // Background workers
        services.AddHostedService<DocumentProcessingWorker>();
        services.AddHostedService<DocumentVectorBootstrapper>();
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        // Boot report with configuration summary
        report.AddModule(ModuleName, ModuleVersion);
        // ... configuration reporting
    }
}
```

### 4. Configuration Model

| Section | Purpose | Sample Keys |
|---------|---------|-------------|
| `DocMind:Processing` | Worker batch size, retry policy, concurrency | `WorkerBatchSize`, `MaxConcurrency`, `MaxRetryAttempts`, `PollIntervalSeconds` |
| `DocMind:Storage` | Storage provider selection | `Kind=FileSystem`, `Bucket`, `RootPath` |
| `DocMind:Vision` | Toggle vision processing | `Enabled`, `Model`, `MaxImagePixels` |
| `DocMind:Embedding` | Embedding provider configuration | `Provider=Weaviate`, `Endpoint`, `ApiKey` |

Use Koan's capability detection to disable features when providers are unavailable (e.g., skip embeddings when Weaviate container is stopped).

### 4.1 Automatic Data Adapter Resolution
The Koan Framework automatically resolves data adapters and table mappings without requiring explicit `[DataAdapter]` or `[Table]` attributes:

- **Core Entities**: `SourceDocument`, `DocumentChunk`, `DocumentInsight`, etc. use automatic adapter selection based on available providers
- **Vector Entities**: `SemanticTypeEmbedding`, `DocumentChunkEmbedding` explicitly use `[VectorAdapter("weaviate")]` for vector storage
- **Provider Priority**: Framework detects available providers (MongoDB, PostgreSQL, etc.) and selects appropriate adapters
- **Graceful Degradation**: Features automatically disable when required providers are unavailable

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
