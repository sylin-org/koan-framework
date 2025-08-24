# Sora.Storage Technical Notes

Contract
- IStorageService orchestrates provider lookup and object lifecycle.
- Auto-registration: This assembly exposes Initialization/SoraAutoRegistrar implementing ISoraAutoRegistrar. Options bind from Sora:Storage and IStorageService is registered if missing.
- Constants: Configuration paths are centralized under Storage.Infrastructure.Constants per ARCH-0040.
- Providers implement IStorageProvider and optional capabilities:
  - IStatOperations.HeadAsync → ObjectStat
  - IServerSideCopy.CopyAsync → server-side copy
  - IPresignOperations.PresignRead/Write → presigned URLs

Routing and fallbacks
- Profiles map logical names to a provider+container.
- DefaultProfile (optional) is used when callers omit the profile.
- FallbackMode governs behavior if DefaultProfile is not set:
  - SingleProfileOnly: allow implicit fallback only when a single profile exists; otherwise throw.
  - Disabled / NamedDefault: require explicit profile or configure DefaultProfile.
- ValidateOnStart enforces invariants at startup (providers exist, containers set, default exists if provided).

Hashing and metadata
- For seekable uploads, SHA-256 is computed by the orchestrator.
- HeadAsync returns lightweight ObjectStat when the provider supports it; otherwise the service may fall back to a short open-read.

Copy/Move semantics
- TransferToProfileAsync(source, container, key, target, targetContainer?, deleteSource=false) is the core operation.
- Helpers:
  - CopyTo(...) → deleteSource=false
  - MoveTo(...) → deleteSource=true
- If both profiles use the same provider and it implements IServerSideCopy, server-side copy is used; otherwise the service streams from source to target.

Presign
- PresignRead/Write calls are routed only when the provider implements IPresignOperations; otherwise NotSupported is thrown.

Safety & performance
- Local provider sanitizes keys, uses temp+rename for atomic writes, supports range reads.
- For large downloads, prefer ReadRangeAsync and set appropriate HTTP headers in web controllers.

Testing
- Unit tests cover write/read/range/delete, exists/head, transfer, onboarding from file/URL, and copy/move helpers.

References
- docs/reference/storage.md
- docs/decisions/STOR-0001-storage-module-and-contracts.md
- docs/decisions/STOR-0006-storage-default-routing-and-fallbacks.md
- docs/decisions/STOR-0007-storage-dx-helpers.md
