# Sylin.Koan.Data.Backup

This capability is shelved and is not a greenfield application choice. Its source remains in the
repository; package presence does not imply support or publication maturity.

Use the generated [product surface](../../docs/reference/product-surface.md) as the authority. The
retained [technical companion](TECHNICAL.md) describes the experimental archive format and failure
ordering for maintainers; it is not application guidance.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Backup
```

## What it adds

Integrity-checked, provider-bounded Entity archive creation and recovery through Koan Storage.

## Limits

Configuration decides participation; unsupported requests reject before provider work with a named capability and a correction. Provider-specific limits live in the package's TECHNICAL.md.
