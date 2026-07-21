# Sylin.Koan.AI.Models

Discover, acquire, catalog, transform, deploy, and inspect models through the capabilities of referenced AI adapters.

```bash
dotnet add package Sylin.Koan.AI.Models
```

## Meaningful use

Reference the package, keep `AddKoan()`, and call the facade from a running host:

```csharp
var matches = await Model.Search(
    new ModelQuery { Keywords = "bge embedding", Format = ModelFormat.ONNX },
    cancellationToken);

var model = await Model.Pull(
    "BAAI/bge-small-en-v1.5",
    format: ModelFormat.ONNX,
    progress: new Progress<ModelPullProgress>(p => Console.WriteLine($"{p.Phase}: {p.Percent:P0}")),
    ct: cancellationToken);
```

`Inspect`, `List`, `Routes`, `Health`, `History`, and `Audit` inspect current catalog/runtime facts. `Convert`,
`Quantize`, `Merge`, `Deploy`, `Rollback`, `Remove`, and `Prune` are explicit mutations routed to capable adapters.

## Guarantees and limitations

- Reference plus `AddKoan()` automatically registers `IModelService`; adapters remain separate reference decisions.
- Each operation selects adapters by declared capability. Absence, ambiguity, unsupported format/runtime, invalid model
  identity, or missing local file fails with a correction; the service does not pretend every adapter can transform or
  deploy every model.
- Catalog entries are Koan Data Entities. Durable history/catalog behavior therefore requires an application-selected
  Data provider. Downloads and runtime deployment remain adapter-owned external effects.
- Conversion/quantization return `JobRef`; this package does not itself guarantee a worker/toolchain, transactional
  rollback across adapters, license suitability, malware scanning, model quality, or deterministic runtime output.

See [TECHNICAL.md](TECHNICAL.md) for capability routing, persistence, and lifecycle boundaries.
