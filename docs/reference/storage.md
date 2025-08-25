# Storage (Sora.Storage + Sora.Web.Storage)

Contract

- Inputs: stream or multipart upload; metadata (FileName, ContentType, Tags, CustomMetadata), optional target profile.
- Outputs: `StorageObject` metadata (Id, Size, ContentHash, ProfileName, ProviderKey, BlobKey), content streams, audit events.
- Errors: 413 size limit, 415 media type, 412/304 preconditions, 416 range unsupported, 423/409 quarantine/pending, policy violations with reason codes.
- Success: 201 Created (sync) or 202 Accepted (async) on upload; 200/206 on content; ETag and caching headers as applicable.

Core pieces

- Entity and contracts (core)
  - `IStorageObject` (properties only); `StorageObject` canonical model with static data access (`All/Query/FirstPage/Page/AllStream/QueryStream`).
  - `IStorageProvider` (IO-only; capabilities for sequential/seek/range and presigned URLs).
  - `IStorageService` (save/open/delete/transfer with hash verification and metadata updates).
  - `IStorageRouter` (profiles + ordered rules; first-match-wins).
  - `IStoragePipelineStep` (OnReceive/OnCommit with outcomes: Continue/Stop/Quarantine/Reroute/Mutate).
  - `IStorageAuditSink` (structured events).

- Web module
  - `StorageController<T>` (T : IEntity, IStorageObject) routes: create/upload, metadata, content, download, tags, transfer, delete.
  - Correct HTTP semantics: Range/ETag/If-None-Match, Accept-Ranges, Content-Disposition, presigned redirects when enabled.
  - Quarantine/Pending behavior surfaced via status codes and headers.

Provider guidance

- Adapters must be thin: implement IO and capabilities only. No routing, policy, or audit logic inside providers.
- LocalStorageProvider ships first. Cloud providers (S3, Azure Blob, GCS) follow the same contract.
- Prefer streaming; only require temp-file staging when a pipeline step needs full-blob inspection.

Profiles and rules

- Profiles map names to provider+container with policy knobs (audit, staging, encryption, retention).
- Rules select a profile based on tags, content-type, size, or custom hints. First match wins; default rule required.

Defaults and fallbacks (minimal config)

- Options:
  - Sora:Storage:DefaultProfile: string (optional).
  - Sora:Storage:FallbackMode: Disabled | SingleProfileOnly | NamedDefault (default SingleProfileOnly).
- Resolution order when the caller does not specify a profile:
  1) DefaultProfile when set (must exist in Profiles).
  2) If FallbackMode == SingleProfileOnly and exactly one profile exists, use it and log a warning.
  3) Otherwise fail with a clear error instructing to set DefaultProfile or specify a profile.
- Recommended: enable default/fallback in Development; require explicit default/rules in Production.

Examples (minimal)

- Single profile, dev convenience:
  - Sora:Storage:Profiles:solo: { Provider: local, Container: only }
  - Implicit fallback used; warning logged on first use.
- Explicit default:
  - Sora:Storage:DefaultProfile: main
  - Sora:Storage:Profiles:main: { Provider: local, Container: bucket }

Helpers (DX)

- Write/onboard:
  - CreateTextFile(key, content, contentType?, profile?, container?)
  - CreateJson(key, value, options?, profile?, container?, contentType=application/json)
  - Create(key, bytes, contentType?, profile?, container?)
  - Create(key, JObject obj, contentType=application/json; charset=utf-8, profile?, container?)
  - CreateJson(key, jsonString, profile?, container?, contentType=application/json; charset=utf-8)
  - Onboard(key, stream, contentType?, profile?, container?)
  - OnboardFile(filePath, key?, contentType?, profile?, container?)
  - OnboardUrl(uri, key?, contentType?, http?, profile?, container?, maxBytes?)
- Read:
  - ReadAllText(profile, container, key, encoding?)
  - ReadAllBytes(profile, container, key)
  - ReadRangeAsString(profile, container, key, from, to, encoding?)
- Probes and orchestration:
  - ExistsAsync(profile, container, key)
  - HeadAsync(profile, container, key) → ObjectStat(Length, ContentType, LastModified, ETag)
  - TransferToProfileAsync(sourceProfile, sourceContainer, key, targetProfile, targetContainer?, deleteSource=false)
  - TransferToProfile(...) helper extension mirrors Async method
  - CopyTo(sourceProfile, sourceContainer, key, targetProfile, targetContainer?) → convenience for deleteSource=false
  - MoveTo(sourceProfile, sourceContainer, key, targetProfile, targetContainer?) → convenience for deleteSource=true
- Lifecycle and routing sugar:
  - TryDelete(profile, container, key), EnsureDeleted(...)
  - InProfile(profile, container?) → fluent wrapper with same helpers

Notes: Helpers are async but omit the Async suffix to keep names terse.

Developer usage (snippets)

- Query by tag:
  - `await StorageObject.Query(q => q.Tags.Contains("medical")).FirstPage(ct);`
- Read content as a stream:
  - `await storageClient.OpenStreamAsync(obj, ct);`
- Transfer to another profile:
  - `await storageService.TransferToProfileAsync("hot", "", obj.Key, "cold");`
  - or via helpers:
    - `await storageService.CopyTo("hot", "", obj.Key, "cold");`
    - `await storageService.MoveTo("hot", "", obj.Key, "cold");`

Provider capabilities

- Capability interfaces extend providers for optional features:
  - `IStatOperations.HeadAsync(container, key)` returns lightweight `ObjectStat` when supported.
  - `IServerSideCopy.CopyAsync(...)` enables server-side copy when source and target are on the same provider.
  - `IPresignOperations.PresignReadAsync/PresignWriteAsync(...)` generate presigned URLs.
- Local provider: supports stat and server-side copy; presigned URLs are not supported. Presign calls will throw NotSupported.

Edge cases and notes

- Large files: prefer 202 + Pending status when scans are async; block reads until Verified or allow privileged access per policy.
- Range unsupported: return 200 with `Accept-Ranges: none`; reject Range with 416.
- Deduplication and resumable uploads are optional follow-ups.

MVP: Local provider specifics

- Capabilities: sequential + seek/range; no presigned URLs.
- Options (example):
  - Sora:Storage:Profiles:
    - { Name: "local-default", ProviderId: "local", BasePath: "C:/sora/storage", Shard: "hash2", AuditEnabled: true }
  - Sora:Storage:Rules:
    - { When: { TagsAny: ["secure"] }, Use: "local-default" }
    - { Default: true, Use: "local-default" }
- Safety: enforce BasePath, sanitize keys, write temp+rename, directory sharding to avoid hot directories.
- Performance: FileOptions.Asynchronous | SequentialScan; atomic rename; range GET supported via seek.

References

- STOR-0001, STOR-0002, STOR-0003, STOR-0004, STOR-0006
- ARCH-0040, WEB-0035, DATA-0061
