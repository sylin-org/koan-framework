# Phase 2: Merge Policies & Conflict Resolution - Detailed Todo List

**Status**: COMPLETE  
**Implementation Window**: 2025-10-21  
**Primary Validation**: `dotnet test tests/S7.Meridian.Tests/S7.Meridian.Tests.csproj`

---

## Task 2.1 – MergePolicy Model & Audit Trail
```
[x] MergePolicy / MergeStrategy models created for decision tracking
[x] MergeDecision entity persists pipeline, strategy, explanation, rule config
[x] DocumentMerger saves MergeDecision per field with rejected extraction ids
```

## Task 2.2 – Transform Registry
```
[x] MergeTransforms registry implements normalizeToUsd, normalizeDateISO, normalizePercent
[x] Fuzzy dedupe, stringToEnum, numberRounding (param support) completed
[x] Exceptions thrown for unknown transforms
[x] Unit tests cover each transform behaviour
```

## Task 2.3 – Merge Resolution Engine
```
[x] DocumentMerger resolves precedence, latest, consensus, collection, highestConfidence
[x] Deterministic tie-breaking (confidence, UpdatedAt, SourceDocumentId)
[x] Evidence metadata records merge strategy + transform applied
[x] Merge decisions logged with explanations + configuration snapshot
```

## Task 2.4 – Field Overrides
```
[x] Overridden fields bypass merge logic with ValueJson = OverrideValueJson
[x] Override confidence forced to 1.0; metadata captures override reason
[x] RunLog entries capture override operations
```

## Task 2.5 – Citations & Deliverable Output
```
[x] Deliverables append citation footnotes (document name, page, snippet)
[x] Merge payload preserves citation markers within template rendering
[x] PDF renderer invoked with transformed markdown
```

---

## Validation
- `MergeTransformsTests` – verifies currency, date, percent normalisation, fuzzy dedupe, enum casing, number rounding.
- `MergePoliciesTests.MergePolicies_ComprehensiveScenario_Works` – multi-document pipeline exercising precedence, consensus, collection union, override logic and citation output.
- `PipelineE2ETests` – regression to ensure existing Phase 1 end-to-end scenario remains green with new merge engine.

```
dotnet test tests/S7.Meridian.Tests/S7.Meridian.Tests.csproj --logger "trx;LogFileName=Phase2.trx"
```
