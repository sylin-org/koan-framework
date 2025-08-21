# Vector provider resolution

Sora decouples vector contracts into `Sora.Data.Vector.Abstractions` and implementation into `Sora.Data.Vector`.
Vector provider selection follows a clear precedence so apps behave predictably.

Precedence (highest wins):

1. Entity-level attribute
   - Decorate your aggregate with `[Sora.Data.Vector.Abstractions.VectorAdapter("weaviate")]` to lock a provider per entity.
2. App defaults
   - Configure `Sora:Data:VectorDefaults:DefaultProvider` (e.g., `"weaviate"`).
   - This is bound by `AddSoraDataVector()` via `VectorDefaultsOptions`.
3. Entity source provider
   - If the entity has `[SourceAdapter("name")]` (or legacy `[DataAdapter("name")]`), that name is used for vectors too.
4. Highest-priority data provider
   - When nothing is specified, the framework picks the highest-priority registered data adapter factory
     (by `ProviderPriorityAttribute`), using its type name (without the `AdapterFactory` suffix) lower-cased.

Notes
- Providers implement `Sora.Data.Vector.Abstractions.IVectorAdapterFactory` and `IVectorSearchRepository<TEntity,TKey>`.
- Add the vector module in DI with `services.AddSoraDataVector()`; it wires defaults and a resolver service.
- Core’s `IDataService.TryGetVectorRepository<TEntity,TKey>()` follows the same precedence and can be used without
  referencing the vector module, as long as the provider factory is registered by the adapter package.

Minimal setup

- Add package reference(s) for your vector adapter(s) (e.g., Weaviate).
- Register the vector module in DI:

```csharp
services.AddSora();
services.AddSoraDataVector();
```

- Optionally set a default provider in configuration:

```json
{
  "Sora": {
    "Data": {
      "VectorDefaults": {
        "DefaultProvider": "weaviate"
      }
    }
  }
}
```

- Or fix an entity to a provider:

```csharp
using Sora.Data.Vector.Abstractions;

[VectorAdapter("weaviate")]
public sealed class Product : IEntity<string>
{
    public string Id { get; set; } = string.Empty;
}
```
