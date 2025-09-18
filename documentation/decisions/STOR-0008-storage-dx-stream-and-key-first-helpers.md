---
id: STOR-0008
slug: STOR-0008-storage-dx-stream-and-key-first-helpers
domain: storage
status: accepted
date: 2025-08-24
title: Storage DX — stream reads and key-first helpers on StorageEntity
---

Context

- Controllers frequently need streaming access (full and ranged) and HEAD by key without instantiating an entity.
- StorageEntity offered text/bytes range-to-string but lacked stream-based helpers and key-first methods.

Decision

- Add instance methods: OpenRead() and OpenReadRange(from?, to?) returning Stream and (Stream, Length).
- Add static, key-first methods: OpenRead(key), OpenReadRange(key, from?, to?), and Head(key) that resolve the type's StorageBinding profile/container.
- Preserve existing Create/Onboard/ReadAllText/ReadAllBytes semantics; do not add Async suffix.

Scope

- Applies to StorageEntity<TEntity> across all storage-bound models, including MediaEntity<TEntity>.
- No change to provider contracts; uses existing IStorageService.ReadAsync/ReadRangeAsync/HeadAsync.

Consequences

- Controllers can implement bytes, HEAD, and range semantics with one-liners and correct routing.
- Improves DX and encourages consistent binding resolution.

Implementation notes

- Methods call ResolveBinding()/InstanceBinding() with container override logic unchanged.
- Follow SoC rules: HTTP semantics (Range/ETag) stay in controllers; StorageEntity offers IO only.

References

- STOR-0007-storage-dx-helpers.md
- STOR-0002-storage-http-endpoints-and-semantics.md