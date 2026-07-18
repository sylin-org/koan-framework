# Sylin.Koan.Storage.Connector.S3

S3-compatible Remote provider for Koan Storage. It works with an explicit endpoint or, when the functional Zen Garden
module is active, can resolve a Garden storage replica lazily at first use.

## Install

```powershell
dotnet add package Sylin.Koan.Storage.Connector.S3
```

For a standalone endpoint, configure the profile and provider:

```json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "main": { "Provider": "s3", "Container": "files" }
      },
      "Providers": {
        "S3": {
          "Endpoint": "http://localhost:9000",
          "Region": "us-east-1"
        }
      }
    }
  }
}
```

Supply credentials through environment variables (`Koan__Storage__Providers__S3__AccessKey` and
`Koan__Storage__Providers__S3__SecretKey`) or another .NET configuration provider; do not commit them.

## Smallest meaningful use

```csharp
using Koan.Storage;
using Koan.Storage.Model;

[StorageBinding("main")]
public sealed class Archive : StorageEntity<Archive> { }

var item = await Archive.Onboard("reports/q3.pdf", reportStream, "application/pdf");
await using var stored = await item.OpenRead();
```

## Guarantees and boundaries

- Exact `Provider: "s3"` selection fails at composition when the connector is absent. Endpoint/credential failures
  remain visible at first backend use.
- Full and range reads currently materialize their response in memory before returning a seekable stream; use bounded
  ranges and do not treat full reads as an unbounded streaming path.
- Stat, recursive listing, same-provider copy, and S3 object operations use the MinIO client.
- Presigned reads/writes use the Moss HTTP presign endpoint, not client-side S3 signing. They require `MossEndpoint`,
  supplied explicitly or through successful Zen Garden discovery.
- Garden discovery is optional layered behavior: the contracts reference is inert; without an active/bound Garden
  client, explicit S3 configuration remains the path.
- Consistency, durability, versioning, encryption, lifecycle, bucket policy, and availability are guarantees of the
  selected S3-compatible service and its configuration.

See [TECHNICAL.md](./TECHNICAL.md) for discovery, bucket naming, capabilities, and resource behavior.
