---
type: SPEC
domain: data
title: "DAC-41 Evaluate and Certify the JSON Entity Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: JSON adapter evaluation prompt
---

# DAC-41 — Evaluate and certify the JSON Entity adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / audit-certification |
| Depends on | DAC-40 |
| Primer scope | dynamically selected JSON manifest |
| Production writes | forbidden |
| Owner | Adapter(JSON); Core KeyValue/Document rows split |

## Meaningful outcome

Koan's file-backed adapter is a deterministic local persistence reference with honest file lifecycle, concurrency,
durability, and query-cost semantics.

## Approved vertical-slice exploration

**Task:** Evaluate JSON as the durable local KeyValue adapter and correct only concrete file/provider boundary gaps.

**Application intent:** Persist ordinary Entities to inspectable local JSON files with no server, while retaining clear
single-process, source-policy, corruption, and query-cost limits.

**Public expression:** Reference `Sylin.Koan.Data.Connector.Json`, call `AddKoan()`, and use `Entity<T>`. The automatic
floor uses `./data`; `Koan:Data:Json:DirectoryPath` or a source `json:DirectoryPath` selects another root. The process
must have the access implied by the source's lifecycle/access policy.

**Guarantee/correction:** Successful writes replace one complete aggregate snapshot and survive a new host. Corrupt
JSON, unavailable paths, unsupported mapping, external shape creation, atomic requirements, and provider-bounded
streaming fail correctively; failed persistence must not become successful in-memory state.

**Complete intent surface:** Package + bootstrap + Entity is complete for the managed default. Directory, named source,
access, and lifecycle configuration appear only when the application chooses those guarantees.

**Public concepts:** `DirectoryPath` is the sole JSON-specific decision. Source, lifecycle, access, partition, batch,
and Entity verbs remain provider-neutral.

**Docs read:** The primer requires four source postures, non-creating External behavior, honest scan receipts, bounded
host state, and exact failure/commit facts. The responsibility map leaves policy with Data, shared query/guards with
KeyValue, and file serialization/locking/replacement with JSON. Architecture principles require one host-owned path and
no storage I/O merely from package availability. DAC-41 specifically requires temp-file fault/concurrency evidence.

**Code read:** `JsonAdapterFactory` resolves a source directory but not lifecycle, access, mappings, or claims;
`JsonRepository` eagerly creates the directory, caches a full file per physical name, mutates memory before persistence,
and serializes writes with per-file semaphores; `JsonHealthContributor` probes selected directories; the suite covers
CRUD, restart, corruption, polymorphism, routing, partitioning, health, batch and managed isolation.

**Reusing:** Data routing/policy; KeyValue query/guard/batch mechanics; naming; Entity JSON polymorphism; temp-directory
fixture; shared conformance oracles. No relational or document-family seam applies.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| one immutable source/file policy passed by the factory | `src/Connectors/Data/Json/JsonAdapterFactory.cs`, `JsonRepository.cs` | enforce Managed/External and access without another runtime path |
| one claim authority if current claims are absent | `src/Connectors/Data/Json/Runtime/JsonFeatures.cs` | prevent factory/repository truth drift |
| file-policy and failed-persist behavior cases | `tests/Suites/Data/Connector.Json/**` | prove non-creation and no failed-write memory leak through public behavior |

**Coalescence:** `KeyValueStore<TEntity,TKey>` remains the shared semantic owner; JSON remains the sole file owner. Keep
one repository path, rebuild its mutation boundary around persist-then-publish or rollback, reject physical mappings it
cannot honor, and delete no useful family behavior. A new filesystem family or generic transaction scaffold is wider
than the proved need.

**Ergonomics:** The managed path remains zero-configuration. Explicit source posture is ordinary Koan configuration;
users never handle locks, temp files, serializers, repositories, or recovery helpers.

**Constraints satisfied:** Entity-first; no HTTP surface; constants/options already exist; file paths are typed options;
no provider-bounded stream claim; no placeholder or alternate persistence route; docs change with observed behavior.

**Risks:** Memory is currently modified before durable replacement, so serialization/cancellation can expose a failed
write until restart. Directory creation currently ignores lifecycle/access, health probing may mutate External sources,
factory claims are absent, and per-physical-name dictionaries/gates are unbounded.

## Execute

1. Pin filesystem/OS, serialization settings, reproducible source/primer fingerprints, and temp/external fixture
   layout; create `evidence/json/`.
2. Inventory activation, file/directory creation, readiness, CRUD/query/batch/instruction, persistence/restart,
   corruption/partial-write handling, concurrency/locking, source policies, isolation, and all alternate paths.
3. Exercise all four source postures. External must never create a file/directory or rewrite shape; read-only rejects
   before file access for the operation.
4. Prove restart durability only if announced. Inject cancellation, disk/full/write/rename failures, malformed data,
   concurrent writers, and cleanup/disposal cases safely in temp storage.
5. Verify scan-backed filter/sort/page claims and receipts are bounded/honest; decline native/index/atomic claims not
   genuinely realized.
6. Run strict shared cells, cold/warm allocation/dispatch baselines, and soak/handle stability.
7. RED creates one-owner remediation cards and blocks; this card never changes production.

## Verification

- Complete JSON suite, strict Forge, packet validator, restart/durability, fault/concurrency, and external-lifecycle
  facts execute on real temp files.

## Definition of done

- [ ] All Observed rows PASS; declines reject through Direct/instruction/batch paths.
- [ ] JSON joins the dockerless PR lane with provider-relative baselines.
- [ ] README/facts describe file ownership and concurrency limits exactly.

## Stop conditions

Stop on destructive non-temp paths, ambiguous crash-consistency claims, shared-family RED, or any required production fix.
