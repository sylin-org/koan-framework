# Koan.Media.Core

## Contract
- **Purpose**: Provide the runtime pipeline for Koan media operations, including variant orchestration, task scheduling, and storage integration.
- **Primary inputs**: Media configuration options, adapter capabilities, and the shared media abstractions.
- **Outputs**: Registered media operators, background pipelines that execute `MediaTask` workloads, and storage routes for asset lifecycle management.
- **Failure modes**: Missing storage adapter registration, unconfigured operators, or variant handlers throwing during execution.
- **Success criteria**: Media assets progress through configured pipelines, operators report health, and storage writes use the appropriate provider profiles.

## Quick start
```csharp
using Koan.Media.Core;
using Koan.Media.Core.Options;

public sealed class MediaAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Media";

    public void Initialize(IServiceCollection services)
    {
        services.AddMediaCore(options =>
        {
            options.DefaultStorageProfile = "cdn";
            options.Pipelines.Add(new MediaPipelineDescriptor
            {
                PipelineId = "video-transcode",
                Operators = { MediaOperatorDescriptor.For<VideoTranscodeOperator>() }
            });
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Media pipelines registered");
}
```
- Call `services.AddMediaCore(...)` inside your auto-registrar to register pipelines, operators, and storage defaults.
- Operators can leverage `MediaAsset.SaveAsync()` or other entity statics to update assets after processing.

## Configuration
- Set `MediaOptions.DefaultStorageProfile` and per-pipeline overrides.
- Register custom operators implementing `IMediaOperator` and describe their capabilities for observability.
- Integrate Koan Storage adapters to route uploads and generated variants.

## Edge cases
- Large concurrent pipelines: configure `MaxConcurrentOperations` to avoid oversaturating resources.
- Operator failures: use retry policies and mark variants with failure metadata to keep asset state consistent.
- Storage latency: prefer streaming uploads for multi-GB media to avoid buffering in memory.
- Multitenancy: scope pipeline IDs and storage profiles per tenant to prevent cross-tenant leaks.

## Related packages
- `Koan.Media.Abstractions` – schema consumed by core pipelines.
- `Koan.Media.Web` – HTTP interface layered atop the core runtime.
- `Koan.Storage` – abstraction for media file persistence.

## Reference
- `MediaOptions` – master configuration for pipelines and storage.
- `IMediaOperator` – contract for implementing operators.
- `MediaPipelineDescriptor` – describes pipeline stages.
