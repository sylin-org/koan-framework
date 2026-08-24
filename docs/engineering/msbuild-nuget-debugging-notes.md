---
type: GUIDE
domain: engineering
title: "MSBuild, NuGet, and NativeAOT debugging notes"
audience: [maintainers, module-authors, framework-authors]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: reviewed
  scope: traps observed while fixing the web+NativeAOT publish blocker (KoanEnhancedModelMetadata);
    each lesson was reproduced in this repository before being written down
---

# MSBuild, NuGet, and NativeAOT debugging notes

Traps hit while making Koan web applications publishable as NativeAOT single binaries
(2026-08-24). Each entry states the symptom, the mechanism, and the rule that would have
skipped the detour. The app-facing story — what an application must do, and what Koan does
for it — lives in [the NativeAOT how-to](../guides/nativeaot-howto.md); this page is for the
person who has to debug the plumbing next time.

## 1. Feature switches are compile-time constants under NativeAOT

Symptom: `AppContext.SetSwitch("...", true)` at startup, and a runtimeconfig knob override,
both had no effect on a published binary.

Mechanism: `[FeatureSwitchDefinition]` lets ILC replace switch reads with constants. The
values arrive as `--feature:` lines in the ILC response file. After publish, nothing runtime-side
can change them.

Rule: to change a feature switch under NativeAOT you must change a **build-time property**.
To see what the compiler actually received, read
`obj/<Configuration>/<Tfm>/<Rid>/native/*.ilc.rsp` and grep `--feature:` — that file answers
in seconds what release notes will not.

## 2. The Web SDK pre-seeds escape-hatch properties

Symptom: a package `.props` setting `<MvcEnhancedModelMetadataSupport Condition="'$(...' ) == ''">true</...>`
had no effect; `-getProperty` returned `false`.

Mechanism: `Microsoft.NET.Sdk.Web.ProjectSystem.targets` seeds the property to `false` when
empty — and imports *after* NuGet's `.props` stage. Any "set if empty" guard in package props
loses to evaluation order against an SDK seed.

Rule: when a NuGet package must pin an SDK-seeded property, set it **unconditionally** from the
package's hand-written `.targets` (which imports after SDK targets, before seed consumers run),
behind a package-specific opt-out sentinel (here: `$(KoanEnhancedModelMetadata)=false`). Never
guard on emptiness; the seed already emptied that possibility. Confirm placement with
`dotnet msbuild -preprocess` — it prints the evaluated import order with line numbers.

## 3. NuGet auto-imports only the conventional asset names

Symptom: a packed `buildTransitive/Sylin.Koan.Core.Aot.props` never executed.

Mechanism: NuGet generates imports for exactly `buildTransitive/<PackageId>.props` and
`buildTransitive/<PackageId>.targets` (plus `build/` for direct references). Any other filename
under those folders is inert payload.

Rule: before designing around a packaging path, check `obj/*.nuget.g.props` / `*.nuget.g.targets`
for the generated Import line. If your file is not listed, it does not run.

## 4. Generated packaging output can own the conventional name

Symptom: our hand-written `buildTransitive/Sylin.Koan.Core.props` packed fine — and never shipped;
the zip contained a 108-byte file we did not write.

Mechanism: Koan's semantic-activation machinery emits `<PackageId>.props` per package
(`KoanActivationNode`). Two items at one `PackagePath` collide silently; last writer wins.

Rule: before claiming a conventional asset name, inventory what the pipeline already generates
(`grep PackagePath` across build targets). Verify packed content directly — open the nupkg and
read the entry; "it compiled" proves nothing about what shipped.

## 5. A minimal-API repro proves nothing about controllers

Symptom: bare `dotnet new webapi` survived the poisoned feature state; Koan apps died on every request.

Mechanism: minimal APIs bypass MVC's model-binder provider chain entirely; controller actions with
parameters go through `SimpleTypeModelBinderProvider`, which reads enhanced-only metadata properties.

Rule: choose the repro harness by the code path under suspicion, not by convenience. A green
adjacent scenario is evidence about that path only.

## 6. Establish upstream posture before engineering a workaround

Ten minutes of searching dotnet/aspnetcore and reading `ModelMetadata.cs` on `main` reclassified
our "defect": MVC controllers are officially unsupported under trimming/AOT ("if it worked, that
was a happy accident"), the switch exists precisely as the sanctioned opt-in, and `main` still
contains the throwing guard — so no servicing patch would ever fix it. That knowledge selected the
fix (Microsoft's own knob, applied centrally) instead of a bespoke binder-provider shim.

Rule: for framework-level failures, search the vendor's tracker and read the vendored source on
their default branch *first*. It changes which fix is legitimate, not just how fast you find it.

## 7. Diagnose MSBuild with its own instruments

The three commands that settled every open question in this arc:

```powershell
dotnet msbuild .\App.csproj -preprocess:out.xml     # true import order, with line numbers
dotnet msbuild .\App.csproj -getProperty:TheProp    # final value after all seeding
Get-Content obj\...\native\*.ilc.rsp | Select-String '--feature:'   # what ILC actually receives
```

Documentation describes intent; these show what the build actually did.
