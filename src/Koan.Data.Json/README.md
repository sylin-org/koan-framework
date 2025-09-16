# Sylin.Koan.Data.Json

JSON file-backed data provider for Koan: simple repository for demos, tests, and local development.

- Target framework: net9.0
- License: Apache-2.0

## Capabilities
- Local file storage with simple filtering and paging semantics
- Great for seed data and smoke tests

## Install

```powershell
dotnet add package Sylin.Koan.Data.Json
```

## Usage
- Use `FirstPage/Page` for UI-like reads; avoid All on large files.
- Prefer first-class statics on your models where available.

## References
- Data patterns: `~/guides/data/all-query-streaming-and-pager.md`
