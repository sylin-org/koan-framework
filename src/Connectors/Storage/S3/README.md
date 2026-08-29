# Sylin.Koan.Storage.Connector.S3

This connector is shelved and is not a greenfield Storage choice. Its source remains in the
repository; package presence does not imply support or publication maturity.

Use the generated [product surface](../../../../docs/reference/product-surface.md) as the authority and
the supported [local Storage path](../../../../docs/reference/storage/index.md) for current application
work. The retained [technical companion](TECHNICAL.md) is maintainer evidence, not an application
setup guide.

## Install

```powershell
dotnet add package Sylin.Koan.Storage.Connector.S3
```

## What it adds

S3-compatible Remote provider for Koan Storage with explicit or optional Zen Garden endpoint discovery.

## Limits

Configuration decides participation; unsupported requests reject before provider work with a named capability and a correction. Provider-specific limits live in the package's TECHNICAL.md.
