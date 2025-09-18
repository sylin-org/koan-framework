---
id: STOR-0005
slug: STOR-0005-storage-local-provider
domain: STOR
title: LocalStorageProvider baseline design and guardrails
status: Accepted
date: 2025-08-24
---

Context

- A local filesystem provider is required for the MVP to enable development, tests, and on-prem deployments.
- It must be safe-by-default (no path traversal), performant, and implement provider capabilities cleanly.

Decision

- Implement Koan.Storage.Local (LocalStorageProvider) as part of the initial release.
- Capabilities
  - SupportsSequentialStream = true
  - SupportsSeek/Range = true (for regular files)
  - SupportsPresignedRead = false
  - SupportsServerSideCopy = true (within the same volume via File.Copy)
- Behavior
  - BasePath required; all keys map under BasePath; path traversal prevented by normalization and rooted checks.
  - Key → path mapping: <BasePath>/<shard>/<key> where shard can be a short prefix (e.g., first 2 bytes of ContentHash) to avoid hot directories; configurable.
  - Write: ensure directory; create temp file (GUID.tmp); stream content while hashing; fsync/flush; atomic rename to final path; set last-write time and optional readonly attribute.
  - OpenRead: FileStream with FileOptions.Asynchronous | SequentialScan; allow FileShare.Read; expose Length and IsSeekable.
  - TryOpenRange: open and Seek; return 206-capable handle when supported; reject if file locked or not seekable.
  - Delete: best-effort removal with retries; return bool.
  - Copy: File.Copy to destination path (may cross profiles if both are Local with different bases); otherwise orchestrator performs stream copy.
  - Properties: stat from filesystem (size, lastModified); ETag not from filesystem—use entity concurrency token.

Scope

- In scope: single-node local filesystem behavior, not network shares (SMB) semantics. No dedup at provider level.

Implementation notes

- Normalize and validate keys; block .. and invalid characters; optionally URL-safe Base64 or hex for hash-derived keys.
- Directory creation should be safe under concurrency.
- Use temp-file suffixes and handle crash recovery by cleaning orphan temp files on startup (optional).
- Windows vs Linux path differences handled via .NET APIs; tests must run cross-platform.

Consequences

- Positive: fast, simple baseline with correct range support and atomic writes.
- Negative: no presigned URL support; large-file performance depends on disk throughput; clustering requires shared storage or a different provider.

References

- STOR-0001 module and contracts
- STOR-0002 HTTP endpoints
