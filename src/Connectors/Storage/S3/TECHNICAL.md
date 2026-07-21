# Sylin.Koan.Storage.Connector.S3 technical contract

## Activation and dependency boundary

`S3StorageModule` binds `Koan:Storage:Providers:S3`, registers one Remote `IStorageProvider`, and reports safe endpoint,
bucket-prefix, and capability posture. Its Zen Garden dependency is `Sylin.Koan.ZenGarden.Contracts`; that contract
assembly has no module and cannot activate Garden functionality.

The connector owns MinIO/Moss protocol conversion and lazy endpoint readiness. Storage owns profile election,
replication composition, segmentation, and transfer policy.

## Resolution order

1. Use explicit `Endpoint`, credentials, SSL, and region settings when supplied.
2. Otherwise read the active `IZenGardenClient.BoundEndpoint` at first use.
3. Query the bound Moss storage catalog for `ReplicaSet` (default `storage`).
4. If necessary, query the reported primary stone and apply its S3 endpoint/credentials.
5. If neither path resolves an endpoint, fail with the explicit configuration correction.

`BucketPrefix` is explicit when configured; otherwise the provider derives it lazily from `AppHost.Identity.Code`.
The physical bucket is `<prefix>-<container>` when a prefix exists.

## Capability and IO posture

The provider declares sequential read, seek, stat, list, server-side copy, and presigned read/write capabilities and
implements all matching optional interfaces. Capability means the adapter has the mechanism; endpoint/credential/Moss
readiness is reported through configuration, failures, and future health—not by changing the compiled capability set.

Uploads stream through the MinIO SDK. Full reads and ranged reads buffer into `MemoryStream`; this makes returned
streams seekable but bounds practical object/range size by process memory. Listing is recursive and backend-consistent
only to the degree the service guarantees. `Exists` returns false for missing buckets/objects but no longer converts
arbitrary connectivity or authorization failures into false.

## Presign and resources

Presign calls POST to `<MossEndpoint>/api/v1/storage/s3/presign`; absent Moss readiness throws
`NotSupportedException`. The singleton provider resolves and memoizes one MinIO client per host plus one presign
`HttpClient`; Storage configuration is a composition-time input, not a hidden live-reload contract. Both retained
clients are disposed with the host. Secrets and full response bodies are not part of composition facts.
