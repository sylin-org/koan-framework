# Relationship Metadata Registration

## Contract
- Only the central Sora.Data AutoRegistrar is responsible for scanning all loaded assemblies for entity models with `[Parent]` attributes.
- Registers a singleton `IRelationshipMetadata` that caches all discovered relationships for the entire app domain.
- Other module registrars do not perform scanning or registration of relationship metadata.
- The Describe method in Sora.Data reports discovered relationships and settings to boot diagnostics.
- Compile-time validation (optional): Source generator or analyzer ensures parent entity keys exist.
- Relationship metadata is available for navigation and endpoint discovery.

## Implementation
- `RelationshipMetadataService` statically scans all loaded assemblies for `[Parent]` attributes and builds parent/child graphs.
- Registration is performed via `AddSoraDataCore()` (or `AddSora()`), which adds the singleton service.
- Manual registration in samples or other modules is unnecessary and should be removed.

## Edge Cases
- If no `[Parent]` attributes are present, the metadata service will return empty graphs.
- All relationship metadata is available app-wide; no per-module duplication.

## References
- proposals/parent-attribute-specification.md
- proposals/parent-key-direct-migration-plan.md
- decisions/DATA-0072-parent-relationship-attribute-explicit-type.md
- src/Sora.Data.Core/Relationships/RelationshipMetadataService.cs
- src/Sora.Data.Core/ServiceCollectionExtensions.cs
- src/Sora.Data.Core/Initialization/SoraAutoRegistrar.cs

## Example
```csharp
// Correct registration (centralized)
services.AddSora(); // or services.AddSoraDataCore();
// No need for manual registration in samples
```

---
This document supersedes any prior guidance suggesting multiple registrars or manual registration for relationship metadata.
