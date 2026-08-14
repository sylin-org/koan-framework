---
type: ADR
domain: data
title: "DATA-0112 - JSON individual-file persistence"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-08-13
framework_version: source-first
validation:
  date_last_tested: 2026-08-13
  status: verified
  scope: JSON adapter layout, path containment, persistence, and focused provider evidence
---

# DATA-0112 — JSON individual-file persistence

## Application intent

A local-first application can keep each Entity in an independently addressable JSON file so Git and other
file-oriented workflows can select, review, commit, recover, and merge one Entity without co-committing unrelated
Entity changes.

## Public expression

The existing aggregate file remains the compatibility default. An application selects independent files through the
JSON source it already owns:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "Default": {
          "Adapter": "json",
          "json": {
            "DirectoryPath": "/workspace/src/writing",
            "Layout": "IndividualFiles",
            "IndividualFilePath": "{id}/article.json"
          }
        }
      }
    }
  }
}
```

Application access remains `Entity<T>` and `EntityController<T>`. No repository registration, project reference,
custom factory, or application file I/O is required. The complete action surface is the published
`Sylin.Koan.Data.Connector.Json` package, ordinary `AddKoan()`, the Entity model, and the two source settings above.

## Guarantee and correction

`Layout = Aggregate` retains the existing one-array-file behavior. `Layout = IndividualFiles` guarantees that a
successful single-Entity upsert or remove mutates only that Entity's owned JSON path. Each persisted file is one JSON
object, uses the shared Entity-family and managed-field codecs, is bounded independently, and is replaced through a
same-directory temporary file.

`IndividualFilePath` is a relative template with exactly one `{id}` token and an optional `{storage}` token. Koan
encodes token values as safe path segments, canonicalizes the result beneath `DirectoryPath`, and rejects rooted,
escaping, ambiguous, or malformed templates before storage mutation. A persisted document whose identity does not
map back to its own path is corrupt and fails correctively rather than becoming a different record.

Individual-file scans enumerate only paths matching the Entity's rendered template and stop materialization at the
requested candidate bound. Bulk operations are multiple independent file mutations and do not claim atomicity or the
aggregate layout's one-replacement optimization. External filesystem writers and cross-process compare-and-swap are
outside this layout guarantee.

## Public concepts

- `JsonStorageLayout.Aggregate` — preserve the existing aggregate-file storage and performance contract.
- `JsonStorageLayout.IndividualFiles` — make one Entity equal one independently mutable JSON file.
- `JsonDataOptions.IndividualFilePath` — express application-owned placement without teaching Koan an application's
  article, media, or publishing schema.

No framework metadata-bag type is added. Applications that preserve unknown top-level JSON members use Json.NET's
standard `[JsonExtensionData]` dictionary; Koan's shared Entity JSON resolver already honors it and protects reserved
framework fields.

## Focused discovery and coalescence assessment

- Current owner and consumers: the JSON connector owns physical layout; `IDataService`, Entity statics, controllers,
  health, and diagnostics consume the elected repository without layout knowledge.
- State lifetime and hot-path cost: aggregate mode keeps its host-owned immutable snapshot. Individual mode reads the
  addressed file on demand and uses a bounded striped lock pool, avoiding one permanent cache entry per Entity.
- Closest pattern: `Runtime/JsonRepository.cs` owns aggregate snapshots and same-directory replacement.
- Specificity: adapter. Moving layout into Data.Core would make a backend choice framework law; moving it into Tezuri
  would duplicate Koan serialization, managed fields, source policy, and repository semantics.
- Disposition: keep the aggregate implementation; add the individual implementation; absorb shared path/codec
  mechanics only where lifecycle and meaning are identical; delete no compatibility path.
- Target owner: `Sylin.Koan.Data.Connector.Json`. A wider owner has no file-layout meaning; a narrower application
  owner cannot preserve provider guarantees without recreating the adapter.

## Ergonomics

The configuration reads as the business decision: aggregate persistence or individual files. IntelliSense presents
two semantic choices without the brittle `AggregateFile`/`RecordFiles` vocabulary. The optional path template is
visible only when placement matters; its defaults produce `{storage}/{id}.json`. The normal Entity and controller
coding models do not branch.

## Implementation placement

| New code | Location | Justification |
|---|---|---|
| `JsonStorageLayout` | `src/Connectors/Data/Json/JsonStorageLayout.cs` | Public adapter option with one top-level type per file. |
| New option members | `src/Connectors/Data/Json/JsonDataOptions.cs` | The existing typed configuration owner. |
| Individual path compiler | `src/Connectors/Data/Json/Runtime/JsonIndividualFileLocator.cs` | Adapter-internal rendering, matching, encoding, and containment. |
| Individual repository | `src/Connectors/Data/Json/Runtime/JsonIndividualFilesRepository.cs` | The KeyValue backend primitives for the new physical layout. |
| Bounded lock/claim registry | `src/Connectors/Data/Json/Runtime/JsonIndividualFileRegistry.cs` | Host-owned coordination without record-count-proportional retained state. |
| Focused proof | `tests/Suites/Data/Connector.Json/.../JsonIndividualFilesSpec.cs` | Real-host provider evidence for placement, CRUD, external edits, partitions, and failures. |

## Constraints and exclusions

- No HTTP route or controller change.
- No new Data.Core persistence vocabulary.
- No automatic migration between layouts.
- No provider-bounded Entity streaming claim; JSON continues to reject `AllStream` and `QueryStream`.
- No atomic multi-file batch claim.
- No ETag, version-token, or cross-process concurrency claim in this slice.
- Stable strings and tunables remain in connector constants/options.
- README and TECHNICAL remain the current instruction and exact-contract owners.

## Release plan

The change is an additive patch in the connector's existing `0.21` compatibility line. Publication occurs only from
the repository's certified `main` workflow. The release must include the new
`Sylin.Koan.Data.Connector.Json` identity and an updated coherent `Sylin.Koan`/`Sylin.Koan.App` bundle before an
external consumer is told to pin it.
