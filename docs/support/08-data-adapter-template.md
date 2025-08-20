# Data Adapter Template (copy-and-tweak)

Purpose: Provide a minimal, idiomatic starting point for a Sora data adapter (relational/document/etc.). It mirrors the “Data Adapter Acceptance Criteria” and follows Sora conventions (options binding, DI registration, naming, health, observability), and controller-first routing via repositories.

Use this as a reference or copy-and-paste template into a new adapter project.

Notes

- Replace placeholders YourData, YOUR_PROVIDER, connection/config paths.
- Keep capability flags honest; avoid advertising unsupported LINQ/string features.
- Prefer server-side pushdown for paging and filters; fall back safely only when acceptable and bounded.

## Project structure (suggested)

- src/Sora.Data.YourData/
  - Initialization/SoraAutoRegistrar.cs
  - YourDataAdapter.cs (options + factory + repository + batch)
  - Infrastructure/Constants.cs (config keys)
  - Health/YourDataHealthContributor.cs

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
    <!-- Add provider client/driver packages here -->
  </ItemGroup>
</Project>
```

## DI auto-registrar

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.YourData.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.YourData";
    public string ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public void Register(IServiceCollection services)
    {
        services.AddOptions<YourDataOptions>().BindConfiguration("Sora:Data:YourData");
        services.AddSingleton<IDataAdapterFactory, YourDataAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INamingDefaultsProvider, YourDataNamingDefaultsProvider>());
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, YourDataHealthContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, Microsoft.Extensions.Hosting.IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Data:YourData");
        report.AddConfig("YourData:Endpoint", opts["Endpoint"], sensitive: true);
    }
}
```

## Options and constants

```csharp
namespace Sora.Data.YourData;

public sealed class YourDataOptions
{
    public string? Endpoint { get; set; }          // or ConnectionString
    public string? ApiKey { get; set; }
    public int DefaultPageSize { get; set; } = 50;
    public int MaxPageSize { get; set; } = 500;
}

internal static class Constants
{
    public static class Config
    {
        public const string Section = "Sora:Data:YourData";
    }
}
```

## Factory

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;

namespace Sora.Data.YourData;

[ProviderPriority(0)]
public sealed class YourDataAdapterFactory : IDataAdapterFactory
{
    private const string ProviderId = "yourdata"; // normalized provider id used by [DataAdapter] or auto selection
    public bool CanHandle(string provider) => string.Equals(provider, ProviderId, StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var opts = sp.GetRequiredService<IOptions<YourDataOptions>>().Value;
        var naming = sp.GetRequiredService<Sora.Data.Abstractions.Naming.IStorageNameResolver>();
        var tracer = new System.Diagnostics.ActivitySource("Sora.Data.YourData");
        return new YourDataRepository<TEntity, TKey>(opts, naming, tracer, sp);
    }
}
```

## Repository skeleton (CRUD + query + batch + instructions)

```csharp
using System.Diagnostics;
using System.Linq.Expressions;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.YourData;

public sealed class YourDataRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,         // optional if you support LINQ
    IStringQueryRepository<TEntity, TKey>,       // optional if you support string queries
    IWriteCapabilities, IQueryCapabilities       // advertise capabilities honestly
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly YourDataOptions _options;
    private readonly IStorageNameResolver _names;
    private readonly ActivitySource _activity;
    private readonly IServiceProvider _sp;

    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete | WriteCapabilities.AtomicBatch;
    public QueryCapabilities Capabilities => QueryCapabilities.Linq; // or String|Linq as applicable

    public YourDataRepository(YourDataOptions options, IStorageNameResolver names, ActivitySource activity, IServiceProvider sp)
    { _options = options; _names = names; _activity = activity; _sp = sp; }

    private string SetName => _names.Resolve<TEntity>(setSuffix: null);

    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.get");
        // TODO: provider read by id
        return Task.FromResult<TEntity?>(null);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.query");
        // TODO: provider-agnostic query object (often ignored); must apply paging defaults
        return Task.FromResult<IReadOnlyList<TEntity>>(Array.Empty<TEntity>());
    }

    public Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.count");
        // TODO: return accurate count if feasible; otherwise 0/throw per contract
        return Task.FromResult(0);
    }

    public Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.upsert");
        // TODO: insert or update by model.Identifier
        return Task.FromResult(model);
    }

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.delete");
        // TODO
        return Task.FromResult(true);
    }

    public Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.upsertMany");
        // TODO: native bulk if supported; else efficient batching
        return Task.FromResult(0);
    }

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.deleteMany");
        // TODO: native bulk if supported; else batching
        return Task.FromResult(0);
    }

    public Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.clear");
        // TODO: provider-specific clear/delete all (use caution)
        return Task.FromResult(0);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new YourDataBatch(this);

    // Optional: LINQ queries
    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.query.linq");
        // TODO: translate predicate to provider query and push down paging
        return Task.FromResult<IReadOnlyList<TEntity>>(Array.Empty<TEntity>());
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.count.linq");
        // TODO
        return Task.FromResult(0);
    }

    // Optional: string queries
    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default)
        => QueryAsync(query, parameters: null, ct);

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.query.string");
        // TODO: parameterized execution; avoid injection
        return Task.FromResult<IReadOnlyList<TEntity>>(Array.Empty<TEntity>());
    }

    public Task<int> CountAsync(string query, CancellationToken ct = default) => CountAsync(query, parameters: null, ct);
    public Task<int> CountAsync(string query, object? parameters, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.count.string");
        // TODO
        return Task.FromResult(0);
    }

    // Instruction execution (ensure-created, raw commands)
    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        using var _ = _activity.StartActivity("data.instruction");
        return instruction.Name switch
        {
            // For relational adapters consider RelationalInstructions.SchemaEnsureCreated
            // For generic adapters you can support a provider-specific ensureCreated
            "data.ensureCreated" => await EnsureCreatedAsync<TResult>(ct),
            _ => throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by YourData.")
        };
    }

    private Task<TResult> EnsureCreatedAsync<TResult>(CancellationToken ct)
    {
        // TODO: create schema/collection if missing; idempotent
        object result = true;
        return Task.FromResult((TResult)result);
    }

    private sealed class YourDataBatch : IBatchSet<TEntity, TKey>
    {
        private readonly List<(string Op, object Arg)> _ops = new();
        private readonly YourDataRepository<TEntity, TKey> _repo;
        public YourDataBatch(YourDataRepository<TEntity, TKey> repo) => _repo = repo;
        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _ops.Add(("add", entity)); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _ops.Add(("upd", entity)); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _ops.Add(("mut", (id, mutate))); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _ops.Add(("del", id!)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _ops.Add(("clr", new object())); return this; }
        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            // TODO: if RequireAtomic and provider supports transactions, wrap; else best-effort
            int added = 0, updated = 0, deleted = 0;
            foreach (var (op, arg) in _ops)
            {
                switch (op)
                {
                    case "add": await _repo.UpsertAsync((TEntity)arg, ct); added++; break;
                    case "upd": await _repo.UpsertAsync((TEntity)arg, ct); updated++; break;
                    case "mut": var (id, mut) = ((TKey, Action<TEntity>))arg; var e = await _repo.GetAsync(id, ct) ?? throw new InvalidOperationException("Not found"); mut(e); await _repo.UpsertAsync(e, ct); updated++; break;
                    case "del": await _repo.DeleteAsync((TKey)arg, ct); deleted++; break;
                    case "clr": await _repo.DeleteAllAsync(ct); break;
                }
            }
            return new BatchResult(added, updated, deleted);
        }
    }
}
```

## Health contributor

```csharp
using Sora.Data.Abstractions;

namespace Sora.Data.YourData;

public sealed class YourDataHealthContributor : IHealthContributor
{
    public string Name => "yourdata";
    public async Task<(bool Healthy, string? Details)> CheckAsync(CancellationToken ct = default)
    {
        // TODO: minimal ping/command against provider
        return await Task.FromResult((true, "ok"));
    }
}
```

## Naming defaults (optional)

```csharp
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.YourData;

public sealed class YourDataNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Kind => "yourdata";
    public string FormatSetName(string baseName) => baseName; // adjust to engine
}
```

## Usage snippet

```csharp
var services = new ServiceCollection().AddSora();
// Adapter package auto-registers via SoraAutoRegistrar
var sp = services.StartSora();

var data = sp.GetRequiredService<IDataService>();
var repo = data.GetRepository<Person, string>();
var page = await repo.QueryAsync(new { /* filter */ }, ct);
```

## Checklist to customize

- Provider id (YourDataAdapterFactory.ProviderId)
- Options (Endpoint/ConnectionString, ApiKey, paging defaults)
- CRUD and query pushdown, safe paging, accurate CountAsync
- Batch semantics honoring RequireAtomic when supported
- Instruction mapping for ensureCreated and supported raw commands
- Capability flags (IQueryCapabilities/IWriteCapabilities) consistent with support
- Health probe and naming defaults
- Tests per 08-data-adapter-acceptance-criteria
