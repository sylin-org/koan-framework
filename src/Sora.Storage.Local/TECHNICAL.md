# Sora.Storage.Local Technical Notes

Options
- BasePath (Sora:Storage:Providers:Local:BasePath): absolute path; provider ensures it exists.

Path layout
- <BasePath>/<container>/<normalized-key>
- Keys are normalized to forward slashes and trimmed; traversal sequences are rejected.

Implementation
- PutAsync: writes to temp then renames; ensures directories.
- ReadAsync/ReadRangeAsync: opens streams with FileOptions.Asynchronous and SequentialScan; validates ranges.
- DeleteAsync: removes file if exists; returns success flag.
- HeadAsync: implemented (IStatOperations).
- CopyAsync: implemented (IServerSideCopy) using File.Copy with overwrite.

Limits
- Presign not supported.
- No automatic sharding beyond container/key path; callers can incorporate prefixes if needed.

Tests
- Covered by Sora.Storage.Tests with Local provider scenarios including range reads, exists/head, transfer, onboarding, and copy/move.

Auto-registration
- This assembly exposes Initialization/SoraAutoRegistrar which binds Sora:Storage:Providers:Local and registers LocalStorageProvider with DI.
