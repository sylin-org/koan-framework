# Sylin.Koan.AI.Contracts.Shared — technical contract

## Responsibility

This package is a dependency-free exchange boundary for AI lifecycle extensions. It owns immutable identity,
requirement, status, score, and lineage shapes; it owns no runtime mechanics.

## Vocabulary

- `ModelRef`, `DatasetRef`, and `JobRef` carry bounded identities and optional snapshot state.
- `ComputeRequirement` expresses accelerator, minimum VRAM, location, and preferred-node intent.
- `EvalScore` and `EvalResult` carry metric outcomes and optional baseline comparison.
- `Lineage` connects source models, datasets, operations, and metadata.
- `Accelerator`, `ModelFormat`, `Quantization`, `JobStatus`, `ComputeLocation`, `ModelCapability`, and `ModelOrigin`
  provide stable serialized categories for those shapes.

## Boundary

The package deliberately does not depend on `Sylin.Koan.Core`, `Sylin.Koan.AI`, data entities, configuration, DI, or
JSON libraries. Functional model catalogs, compute fabrics, trainers, evaluators, and sibling-repository extensions
may share it without importing one another. Those functional owners define lookup, execution, persistence, polling,
authorization, error, and consistency guarantees.
