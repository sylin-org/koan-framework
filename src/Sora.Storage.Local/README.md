# Sora.Storage.Local

Filesystem-backed storage provider for Sora.Storage. Ideal for development and simple on-prem deployments.

What it does
- Saves/reads/deletes objects on a configured BasePath and container subfolders.
- Supports seek and range reads; implements HeadAsync and server-side copy.
- Does not support presigned URLs.

Register
- Auto-registers via SoraAutoRegistrar. Ensure SoraInitialization.InitializeModules(services) is called at startup.
- Required option: Sora:Storage:Providers:Local:BasePath → an absolute directory path.

Profile example
- Sora:Storage:Profiles:main: { Provider: local, Container: bucket }
- Sora:Storage:Providers:Local:BasePath: C:/data/sora

Capabilities
- IStatOperations (HeadAsync): uses FileInfo for lightweight stat.
- IServerSideCopy (CopyAsync): uses File.Copy for fast intra-provider transfers.
- IPresignOperations: not implemented (will throw NotSupported via orchestrator).

Safety
- Key sanitization to prevent traversal.
- Temp file + atomic rename on write.
- Directory creation as needed.

Notes
- Use this provider for local dev and testing. For cloud scenarios, use an S3/Azure/GCS provider implementing presign.
