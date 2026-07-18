# Sylin.Koan.Storage.Connector.Local technical contract

## Activation and options

`LocalStorageModule` binds `Koan:Storage:Providers:Local`, registers `LocalStorageProvider`, and reports its resolved
configuration/capability posture. `BasePath` is required; relative paths resolve against the process working directory
once when the singleton provider is constructed.

## Physical layout and safety

Objects live under `<base>/<container>/<sha1-prefix>/<normalized-key>`. The two-byte hash prefix reduces hot
directories without changing the provider key returned to callers. The provider validates the final full path with
`Path.GetRelativePath` against the resolved base, so container or key input cannot escape the root.

Writes copy into a unique sibling temporary file, flush managed buffers, and replace the destination. Cancellation or
failure removes the temporary file. The provider does not promise disk write-through, journal policy, or recovery of
orphan files left by process/host termination between filesystem operations.

## Capability declaration

The provider declares sequential read, seek, stat, list, and server-side copy. It implements the matching optional
interfaces and declares `StorageProviderPlacement.Local`. Presign tokens/interfaces are intentionally absent.

`OpenReadRange` validates inclusive bounds and buffers only the selected range. `ListObjects` walks shard directories,
reconstructs logical provider keys, applies a prefix, ignores temporary files, and tolerates concurrent removal. Copy
uses `File.Copy` within the provider root and overwrites the target.

## Failure semantics

Missing full reads surface the platform `FileNotFoundException`; `Exists` and delete return false for missing objects;
invalid ranges throw `ArgumentOutOfRangeException`; unsafe paths and invalid segments throw corrective
`InvalidOperationException`. Filesystem authorization, capacity, sharing, and IO failures remain visible to callers.
