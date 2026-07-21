# Sylin.Koan.AI.Contracts.Shared

Dependency-free identities and result vocabulary for AI lifecycle extensions that need to exchange model, dataset,
compute, job, evaluation, or lineage information without referencing the Koan AI runtime.

## Install

```powershell
dotnet add package Sylin.Koan.AI.Contracts.Shared
```

Most application code does not need this package directly. Choose it for a library or external capability that
exchanges lifecycle references with Koan-compatible AI tooling.

## Smallest meaningful use

```csharp
using Koan.AI.Contracts.Shared;

ModelRef model = "acme/support-model";
var compute = ComputeRequirement.WithVram(16);
var evaluation = new EvalScore("groundedness", 0.93, Baseline: 0.88);
```

These values can cross package or process boundaries without bringing provider clients, dependency injection, entity
storage, or orchestration behavior with them.

## Guarantees and boundaries

- The package contains BCL-only records and enums; referencing it activates nothing.
- A reference identifies intent and state supplied by another system. It does not verify that a model, dataset, job,
  node, or evaluation exists.
- `ComputeRequirement` describes requested characteristics; it does not elect or reserve compute.
- `JobRef.Status` and evaluation values are snapshots, not live subscriptions.
- The vocabulary is intentionally lifecycle-oriented and separate from inference adapters in
  `Sylin.Koan.AI.Contracts`.

See [TECHNICAL.md](./TECHNICAL.md) for the complete ownership boundary.
