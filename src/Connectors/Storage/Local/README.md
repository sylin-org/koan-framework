# Koan.Storage.Connector.Local

Filesystem-backed storage provider for Koan.Storage. Ideal for development and simple on-prem deployments.

What it does
- Saves/reads/deletes objects on a configured BasePath and container subfolders.
- Supports seek and range reads; implements HeadAsync and server-side copy.
- Does not support presigned URLs.

Register
- Reference the package; `LocalStorageModule` activates through `AddKoan()` with no manual provider wiring.
- Required option: Koan:Storage:Providers:Local:BasePath → an absolute directory path.

Profile example
- Koan:Storage:Profiles:main: { Provider: local, Container: bucket }
- Koan:Storage:Providers:Local:BasePath: C:/data/Koan

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

