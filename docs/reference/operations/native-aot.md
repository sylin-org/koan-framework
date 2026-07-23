---
type: REFERENCE
domain: operations
title: "NativeAOT deployment boundary"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: blocked
  scope: experimental NativeAOT path; current pinned toolchain fails before an executable is produced
---

# NativeAOT deployment boundary

NativeAOT is experimental and is not a supported 0.20 deployment claim. With the pinned SDK/runtime
toolchain, the retained GardenCoop reproduction currently stops inside the ILC analyzer before it
produces an executable. Use self-contained or single-file JIT publication for the current release.

The implementation map remains in the [NativeAOT guide](../../guides/nativeaot-howto.md) so the
upstream/compiler boundary stays reproducible. It must not be read as a working candidate recipe.

For a smaller deployable artifact today:

```powershell
dotnet publish App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The same application composition and Reference-equals-Intent behavior remain in force. A connector's
ordinary support status does not imply that connector is trim- or AOT-safe.
