---
type: GUIDE
domain: performance
title: "GardenCoop NativeAOT How-To"
audience: [developers, architects, operators]
status: current
last_updated: 2025-11-12
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-12
  status: verified
  scope: g1c1-gardencoop
related_guides:
  - performance.md
  - garden-cooperative-journal.md
  - semantic-pipelines.md
---

# GardenCoop NativeAOT Adoption Guide

> **Contract** — Build the `g1c1.GardenCoop` slice as a trimmed NativeAOT binary using only the .NET 10 SDK. No external services are required; SQLite remains the embedded provider. Errors to watch: missing VC++ toolchain on Windows, linker trimming dynamic Koan assets, or cross-platform runtime identifiers lacking ICU/invariant globalization.

This guide documents how we moved the Garden Cooperative slice to NativeAOT and how you can reproduce the flow locally or in CI without guesswork.

---

## 1. Prerequisites

- .NET 10 SDK (10.0.100 or newer)
- Windows with MSVC build tools, or clang/LLD when targeting Linux/macOS
- Koan repository cloned (`samples/guides/g1c1.GardenCoop`)
- Optional: PowerShell 7+ for the helper scripts

The NativeAOT profile relies on the following project settings:

- Opt-in publishing via `-p:PublishAot=true` (the regular `dotnet run` path stays IL for faster iteration)
- Explicit trimming roots via `NativeAotRoots.xml` for controllers, entities, and automation modules
- Invariant globalization to avoid ICU bundles and keep binaries small
- Strip-symbols enabled for leaner deployment artifacts

No additional runtime configuration is required. `builder.Services.AddKoan()` continues to auto-register entities and controllers under NativeAOT.

---

## 2. Build the NativeAOT Binary

```pwsh
cd samples/guides/g1c1.GardenCoop
$rid = "win-x64"   # swap to linux-x64 or osx-arm64 as needed

dotnet publish g1c1.GardenCoop.csproj `
  -c Release `
  -r $rid `
  --self-contained true `
  -p:PublishAot=true
```

On Windows you can shortcut the publish + launch workflow with `start-native.bat`. Pass `--rid linux-x64` (or any other RID) to cross-compile, and append `-- --urls http://localhost:5050` to forward arguments to the native binary.

Key outputs:

- Publish directory: `bin/Release/net10.0/<rid>/native/`
- Executable: `g1c1.GardenCoop[.exe]`
- Size: ~22–28 MB on Windows with symbols stripped

### Build diagnostics

- Expect trim/AOT warnings from ASP.NET Core MVC and the Koan admin/auth modules. They are tracked as part of broader framework work; the slice executes correctly despite the warnings. Capture new warnings in the validation log so we can prioritize suppression or source-generation follow-up.
- Windows developers must have the Desktop Development with C++ workload installed (MSVC, `link.exe`). Linux/macOS users require clang/LLD via `apt`, `brew`, etc.

---

## 3. Run and Verify

```pwsh
$rid = "win-x64"   # match the publish RID
$native = "bin/Release/net10.0/$rid/native/g1c1.GardenCoop.exe"
& $native --urls "http://localhost:5000"
```

Success checklist:

- Console prints the startup banner and sample lifecycle message
- Navigating to `http://localhost:5000` serves the static dashboard from `wwwroot/`
- Plot + Sensor seed data appears immediately (Bed 3 reminder triggers normal journal output)
- `Ctrl+C` or window close exits cleanly without managed runtime warnings

Use `--urls` to override the baseline HTTP address; Kestrel command-line switches still work under NativeAOT.

---

## 4. Edge Cases & Mitigations

| Scenario                            | Symptom                                                                | Resolution                                                                                                                                                          |
| ----------------------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| VC++ build tools missing on Windows | `error MSB8041: Native compilation requires Visual C++`                | Install the **Desktop development with C++** workload (Visual Studio Installer) or the standalone Build Tools.                                                      |
| Linker trimmed a Koan asset         | Runtime `InvalidOperationException` about missing controllers/entities | Add the type to `NativeAotRoots.xml` or annotate with `[DynamicDependency]`; re-run publish.                                                                        |
| Targeting Linux without ICU         | `System.Globalization` errors or missing culture data                  | Keep `<InvariantGlobalization>true</InvariantGlobalization>` (default) or supply ICU via `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0` and include the ICU native libs. |
| SQLite native dependency missing    | Startup throws `DllNotFoundException: e_sqlite3`                       | Ensure `--self-contained true` remain set; `SQLitePCLRaw` bundles native bits automatically in self-contained AOT outputs.                                          |
| Debugging instrumentation required  | No PDBs present in publish directory                                   | Set `-p:StripSymbols=false` or provide `-p:IlcGenerateCompleteDebugInfo=true` temporarily when troubleshooting.                                                     |

---

## 5. Operational Notes

- CI can cache the native publish output by RID; the build is deterministic once the toolchain is installed.
- The development scripts (`start.ps1`, plain `dotnet run`) stay on the IL build for faster iteration. Use `start-native.bat` / `start-native.ps1` or `dotnet publish -p:PublishAot=true` when you explicitly need the NativeAOT binary.
- Additional Koan modules should be explicitly rooted if the sample evolves (messaging workers, new controllers, etc.). Update `NativeAotRoots.xml` each time you add reflection-heavy components.
- Document any new NativeAOT exceptions in `docs/guides/nativeaot-gardencoop-howto.md` and update the validation metadata when re-tested.

---

## 6. Validation Log

- **2025-11-12** — Published `win-x64` NativeAOT binary, executed locally, verified dashboard + reminder automation and graceful shutdown. Known trim/AOT warnings remain from ASP.NET Core MVC + Koan admin/auth modules (JSON serialization, configuration binding); sample run-time behavior validated.

Keep this guide in sync with future Koan releases or when the GardenCoop slice adds new capabilities (e.g., messaging, push notifications) that might introduce additional trimming considerations.
