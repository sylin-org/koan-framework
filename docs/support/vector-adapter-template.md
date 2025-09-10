# Vector Adapter Template (copy-and-tweak)

Purpose: Provide a minimal, idiomatic starting point for a Sora vector adapter. This mirrors the “Vector Adapter Acceptance Criteria” and follows Sora conventions (options binding, DI registration, naming, health, observability).

Use this as a reference or copy-and-paste template into a new adapter project.

Notes

- Replace all placeholders YourVector, YOUR_PROVIDER, and configuration paths as needed.
- Keep capability flags and guardrails honest—don’t advertise features you don’t implement.

## Project structure (suggested)

- src/Sora.Data.YourVector/
  - Initialization/SoraAutoRegistrar.cs
  - YourVectorAdapter.cs (options + factory + repository)
  - Infrastructure/Constants.cs (config keys)
  - Health/YourVectorHealthContributor.cs

## Minimal .csproj snippet

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sora.Data.Abstractions\Sora.Data.Abstractions.csproj" />
    <ProjectReference Include="..\Sora.Data.Core\Sora.Data.Core.csproj" />
    <!-- Add provider SDK client as PackageReference here -->
  </ItemGroup>
</Project>
```

## DI auto-registrar

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.YourVector.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.YourVector";
    public string ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public void Register(IServiceCollection services)
    {
    services.AddSoraOptions<YourVectorOptions>(config, "Sora:Data:YourVector");
        services.AddSingleton<IVectorAdapterFactory, YourVectorAdapterFactory>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, YourVectorHealthContributor>());
        // Provide default naming behavior if your provider needs custom defaults
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, YourVectorNamingDefaultsProvider>());
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Data:YourVector");
        report.AddConfig("YourVector:Endpoint", opts["Endpoint"], sensitive: true);
    }
}
```

## Options and constants

```csharp
namespace Sora.Data.YourVector;

public sealed class YourVectorOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string Metric { get; set; } = "cosine"; // or provider default
    public int? Dimension { get; set; }            // null -> provider-defined
}

internal static class Constants
{
    public static class Config
    {
        public const string Section = "Sora:Data:YourVector";
    }
}
```

## Factory

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;

namespace Sora.Data.YourVector;

// Prefer a clear provider id (e.g., "weaviate", "pinecone").
[ProviderPriority(0)]
public sealed class YourVectorAdapterFactory : IVectorAdapterFactory
{
    private const string ProviderId = "yourvector"; // normalized id used in [DataAdapter] or default selection
    public bool CanHandle(string provider) => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public IVectorSearchRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<YourVectorOptions>>().Value;
        var naming = sp.GetRequiredService<Sora.Data.Abstractions.Naming.IStorageNameResolver>();
        var tracer = new System.Diagnostics.ActivitySource("Sora.Vector.YourVector");
        return new YourVectorRepository<TEntity, TKey>(opts, naming, tracer, sp);
    }
}
```

## Repository skeleton

```csharp
using System.Diagnostics;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;
using Sora.Data.Vector;

namespace Sora.Data.YourVector;

public sealed class YourVectorRepository<TEntity, TKey> : IVectorSearchRepository<TEntity, TKey>, IInstructionExecutor<TEntity>, IVectorCapabilities
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly YourVectorOptions _options;
    private readonly IStorageNameResolver _names;
    private readonly ActivitySource _activity;
    private readonly IServiceProvider _sp;

    // Example capability set: KNN + Filters + PaginationToken
    public VectorCapabilities Capabilities => VectorCapabilities.Knn | VectorCapabilities.Filters | VectorCapabilities.PaginationToken;

    public YourVectorRepository(YourVectorOptions options, IStorageNameResolver names, ActivitySource activity, IServiceProvider sp)
    { _options = options; _names = names; _activity = activity; _sp = sp; }

    private string IndexName => _names.Resolve<TEntity>(setSuffix: null); // honor StorageNameRegistry

    public async Task UpsertAsync(TKey id, float[] embedding, object? metadata = null, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.upsert");
        // TODO: call provider API to upsert vector for (IndexName, id)
        await Task.CompletedTask;
    }

    public async Task<int> UpsertManyAsync(IEnumerable<(TKey Id, float[] Embedding, object? Metadata)> items, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.upsertMany");
        // TODO: bulk upsert if supported; else chunk + parallelize prudently
        return await Task.FromResult(0);
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.delete");
        // TODO: call provider API
        return await Task.FromResult(true);
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.deleteMany");
        // TODO: bulk delete if supported; else chunk
        return await Task.FromResult(0);
    }

    public async Task<VectorQueryResult<TKey>> SearchAsync(VectorQueryOptions options, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.search");
        // Guardrails: clamp TopK according to Sora:Data:VectorDefaults if bound
        var defaults = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.Data.Core.Options.VectorDefaultsOptions>>()?.Value;
        var topK = options.TopK ?? defaults?.DefaultTopK ?? 10;
        var max = defaults?.MaxTopK ?? 200;
        if (topK > max) topK = max;

        // TODO: call provider API with IndexName, options.Query, filter, token, topK
        var matches = new List<VectorMatch<TKey>>();
        string? next = null; // provider continuation token
        return new VectorQueryResult<TKey>(matches, next);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("vector.instruction");
        return instruction.Name switch
        {
            VectorInstructions.IndexEnsureCreated => await EnsureIndexAsync<TResult>(ct),
            VectorInstructions.IndexRebuild      => await RebuildIndexAsync<TResult>(ct),
            VectorInstructions.IndexStats        => await GetStatsAsync<TResult>(ct),
            VectorInstructions.IndexClear        => await ClearIndexAsync<TResult>(ct),
            _ => throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by YourVector.")
        };
    }

    private Task<TResult> EnsureIndexAsync<TResult>(CancellationToken ct)
    {
        // TODO: create class/index if missing; idempotent
        object result = true;
        return Task.FromResult((TResult)result);
    }

    private Task<TResult> RebuildIndexAsync<TResult>(CancellationToken ct)
    {
        // TODO: rebuild if engine supports it
        object result = true;
        return Task.FromResult((TResult)result);
    }

    private Task<TResult> GetStatsAsync<TResult>(CancellationToken ct)
    {
        // TODO: return dictionary or provider-native stats
        object result = new Dictionary<string, object?>{ {"index", IndexName} };
        return Task.FromResult((TResult)result);
    }

    private Task<TResult> ClearIndexAsync<TResult>(CancellationToken ct)
    {
        // TODO: delete all vectors in index (use caution)
        object result = true;
        return Task.FromResult((TResult)result);
    }
}
```

## Health contributor

```csharp
using Sora.Data.Abstractions;

namespace Sora.Data.YourVector;

public sealed class YourVectorHealthContributor : IHealthContributor
{
    public string Name => "yourvector";
    public async Task<(bool Healthy, string? Details)> CheckAsync(CancellationToken ct = default)
    {
        // TODO: ping/version endpoint minimally
        return await Task.FromResult((true, "ok"));
    }
}
```

## Naming defaults (optional)

```csharp
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.YourVector;

public sealed class YourVectorNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Kind => "yourvector"; // influences StorageNameRegistry style when provider is default
    public string FormatSetName(string baseName) => baseName;    // adjust if engine needs suffix/prefix rules
}
```

## Usage snippet

```csharp
var services = new ServiceCollection().AddSora();
// Adapter package auto-registers via SoraAutoRegistrar
var sp = services.StartSora();

var data = sp.GetRequiredService<IDataService>();
var vectors = data.GetRequiredVectorRepository<Person, string>();
var result = await vectors.SearchAsync(new(new float[]{ /* ... */ }, TopK: 20));
```

## Checklist to customize

- Provider id (YourVectorAdapterFactory.ProviderId)
- Options (Endpoint, ApiKey, Metric, Dimension)
- Search, upsert, delete calls to the provider SDK
- Instruction mappings for index lifecycle
- Capability flags consistent with real support
- Health probe and naming defaults
- Tests per acceptance criteria
