---
id: STOR-0001
slug: STOR-0001-storage-module-and-contracts
domain: STOR
title: Storage module, entity, providers, and orchestration contracts
status: Accepted
date: 2025-08-24
---

Context

- Koan needs first-class file storage with entity-backed metadata, thin provider adapters, and centralized orchestration (routing, pipeline, audit, transfers).
- Consumers may use storage without web; HTTP concerns must be in a separate module.
- DX should be simple: model statics for data access and thin helpers for content reads.

Decision

- Introduce Koan.Storage (core, transport-agnostic) and Koan.Web.Storage (HTTP endpoints) as separate modules.
- Define a canonical entity and a properties-only interface:
  - IStorageObject: metadata contract (no IO methods).
  - StorageObject : Entity<StorageObject>, IStorageObject - the default model with first-class static data methods (All/Query/Stream/Page).
- Providers are thin and IO-only:
  - IStorageProvider: OpenRead/OpenRange (optional), Write, Delete, Exists, Copy, GetProperties, PresignRead (optional).
  - Provider capabilities are discoverable (sequential vs seekable/range; presign support).
  - Initial implementation MUST include LocalStorageProvider with: safe root directory enforcement, deterministic key→path mapping, range support where possible, atomic writes via temp+rename, and traversal guards.
- Central orchestration lives in core:
  - IStorageService: Save/Open/Delete/Transfer (hash, integrity, metadata updates, audit emission).
  - IStorageRouter: resolves a Profile (target provider+container) using simple, deterministic rules.
  - IStorageAuditSink: structured events for actions and pipeline steps.
- Options and constants:
  - StorageOptions: Profiles[], Rules[], Limits (size, types), Pipeline defaults.
  - Constants centralized under Koan.Storage.Infrastructure.Constants (non-HTTP); HTTP constants live in Koan.Web.Storage.

Scope

- In scope: entity contracts, provider contracts and capabilities, orchestrator/router/audit contracts, options, and module split. Include Koan.Storage.Connector.Local as the baseline provider in the first iteration.
- Out of scope: specific cloud adapters (to be added as thin providers) and UI integrations.

Contracts (indicative)

- IStorageObject (properties only)
  - Id (from IEntity), FileName, ContentType, Size, ContentHash, ETag/Version,
    ProfileName, ProviderKey, BlobKeyOrUri, Tags (string[]), CustomMetadata (k/v),
    ProcessingStatus (Pending|Verified|Quarantined|Rejected), CreatedAt, UpdatedAt, DeletedAt? (optional).
- IStorageProvider (IO-only)
  - Task<StorageReadHandle> OpenReadAsync(profile, key, CancellationToken)
  - Task<StorageReadHandle?> TryOpenRangeAsync(profile, key, long offset, long? count, CancellationToken)
  - Task<ProviderWriteResult> WriteAsync(profile, key, Stream content, IReadOnlyDictionary<string,string> metadata, CancellationToken)
  - Task<bool> DeleteAsync(profile, key, CancellationToken)
  - Task<bool> ExistsAsync(profile, key, CancellationToken)
  - Task<ProviderObjectProperties?> GetPropertiesAsync(profile, key, CancellationToken)
  - Task<Uri?> GetPresignedReadUrlAsync(profile, key, TimeSpan ttl, CancellationToken)
- Capabilities
  - SupportsSequentialStream, SupportsSeek/Range, SupportsPresignedRead, SupportsServerSideCopy.
- IStorageService (orchestrator)
  - SaveAsync(IStorageObject obj, Stream content, SaveOptions, CancellationToken)
  - OpenAsync(IStorageObject obj, ReadOptions, CancellationToken) → StorageReadHandle
  - DeleteAsync(IStorageObject obj, CancellationToken)
  - TransferAsync(IStorageObject obj, string targetProfile, CancellationToken) → TransferResult
- IStorageRouter
  - ResolveProfileAsync(RoutingContext ctx, CancellationToken) → StorageProfile
- IStorageAuditSink
  - EmitAsync(StorageAuditEvent evt, CancellationToken)

Profiles and rules (summary)

- StorageProfile: Name, ProviderId, Container/Bucket/Path, Encryption/Retention (optional), AuditEnabled.
- Rules: simple predicate list (first-match-wins) over tags, content-type, size, classification; evaluated by IStorageRouter.

Consequences

- Positive: thin adapters, strong SoC, reusable core without web deps, predictable DX via model statics and helpers.
- Negative: more moving parts (profiles, rules, pipeline); requires clear docs, DI helpers, and version coordination with Web module.

Implementation notes

- Compute ContentHash while streaming writes; avoid double reads.
- Prefer temp-file staging only when pipeline steps require full-blob inspection.
- Keep entity free of IO; DX helpers live in an IStorageClient service with optional extension methods.
- Use Background jobs for large transfers; copy-then-switch with hash verification.
- LocalStorageProvider specifics: enforce base path; normalize/validate keys; ensure directories exist; write to temp file then move; open with FileOptions.Asynchronous and FileShare.Read for readers; set ReadOnly attribute post-commit when policy requires immutability; use sparse file support and memory-mapped IO only if justified.

Follow-ups

- STOR-0002 HTTP endpoints and semantics (Web module).
- STOR-0003 Routing rules and profiles.
- STOR-0004 Ingest pipeline and policy steps.

References

- ARCH-0040 Config and constants naming
- WEB-0035 EntityController transformers
- DATA-0061 Data access pagination and streaming

