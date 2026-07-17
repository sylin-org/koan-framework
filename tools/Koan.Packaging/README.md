# Koan packaging

Advancing `dev` is the complete package-release instruction. The protected workflow serializes the
event, finishes any incomplete prior release wave, projects the current source delta onto
`automation/package-lineage-dev`, proves the exact result, and promotes only the identities selected
by that result. Maintainers do not select packages, calculate patch versions, rebuild old public
identities, or carry release state between runs.

The safe read-only diagnostic is:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

Package-product structure and review signals are compiled from that same evaluated graph:

```powershell
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md
```

`quality` is read-only. It derives artifact shape and presentation role from standard project facts,
checks package-owned orientation and shared metadata, and emits stable corrective findings. Its
`structurally-ready` state is not a graduation, maturity, or support claim; R11 architecture, prose,
and clean-consumer evidence remain separate acceptance cells.

The canonical public product surface is compiled from evaluated .NET/NuGet project facts and the
irreducible maturity judgments in `product/claims.json`:

```powershell
dotnet run --project tools/Koan.Packaging -- product-surface `
  --output docs/reference/product-surface.json `
  --markdown docs/reference/product-surface.md
```

The compiler fails on unknown packages, invalid maturity, missing documentation/evidence, duplicate
claims, or a support promotion without a package-owned README. Packages that exist but have no claim
remain explicitly `unassessed`. Release planning runs this validation automatically; operators supply
no product-catalog input.

The protected compilation and proof sequence is:

```powershell
dotnet run --project tools/Koan.Packaging -- lineage `
  --source <dev-source-sha> `
  --previous-source <prior-dev-source-sha> `
  --previous-lineage <prior-lineage-sha> `
  --output artifacts/release/release-lineage.json

dotnet run --project tools/Koan.Packaging -- plan `
  --lineage artifacts/release/release-lineage.json `
  --output artifacts/release/release-set.json

dotnet run --project tools/Koan.Packaging -- pack `
  --manifest artifacts/release/release-set.json `
  --output artifacts/release/packages `
  --clean-room

dotnet run --project tools/Koan.Packaging -- wave-bundle `
  --lineage artifacts/release/release-lineage.json `
  --manifest artifacts/release/release-set.json `
  --artifacts artifacts/release/packages `
  --evidence artifacts/release `
  --output artifacts/release
```

`lineage` intentionally switches a clean checkout to its dedicated local lineage branch and creates
one commit. Run it only in the protected workflow or a disposable rehearsal checkout. Omit
`--previous-lineage` only when initializing lineage. That first projection is an explicit all-owner
bootstrap: every active owner receives a fresh identity. Bootstrap evaluates its predecessor with the
pinned toolchain, so that predecessor must remain evaluable.

Deleting a package project plus its version owner is complete retirement intent. The compiler removes
it from the active graph, permanently records its last package ID/path/version, and plans no artifact.
A retired ID or path cannot return. Renaming a package owner remains unsupported; introduce a new ID
and path and let the old owner retire.

`SourceCommit` identifies the developer's `dev` event. `VersionCommit` identifies the exact linear
projection that owns NBGV identities, SourceLink metadata, packed bytes, and release evidence. The
committed lineage state must match `release-lineage.json` before planning. Lineage schema 3 also keeps
each owner's evaluated source-input map: analyzer ProjectReference sources are automatic, while
external generated payloads use explicit `KoanPackageInput` ownership. Comparison of the prior and
current maps preserves ownership through add, change, delete, and rename without treating ignored
artifacts or local build output as release intent.

Release-manifest schema 4 removes the obsolete package `Kind` field. Package shape is derived only
for the public product surface from standard NuGet/MSBuild facts; it is not release intent.

## Exact release wave

For a fixed set of exact inputs, `wave-bundle` writes two files with deterministic encoding:

- `release-wave-<full-VersionCommit>.zip` contains `release-lineage.json`, `release-set.json`, every
  referenced nupkg/snupkg, and separate FirstUse and GoldenJourney evidence;
- `release-wave.json` binds that ZIP, its inner lineage/manifest hashes, the exact full
  `VersionCommit`, package count, and canonical tag `release/dev/<full-VersionCommit>`.

The bundle is uploaded to one draft GitHub Release before the marker. The uploaded marker is prepared
authority: it is never replaced or rebuilt. A wave derives one of four states:

- `missing`: no Release exists for the exact tag;
- `staging`: a resettable draft exists without an uploaded marker;
- `prepared`: the draft has the exact validated bundle and uploaded marker;
- `published`: the same Release is immutable and has the exact completion receipt and tag target.

Promotion consumes only downloaded, revalidated escrow bytes. It follows manifest dependency order,
pushes a missing nupkg, always replays each required exact snupkg with duplicate-safe semantics, and
waits for every nupkg to become visible. It then uploads one deterministic
`release-completion.json`, creates or verifies the non-forced full-commit tag, and publishes that same
draft. Recovery needs no secondary ledger.

If a push succeeds but a symbol, receipt, or response fails, a later `dev` event recovers the prior
wave first from its original escrow. A completion asset left in GitHub's `starter` state is the only
asset repaired in place; uploaded authority is never overwritten. If any selected nupkg is public
without exact prepared escrow, recovery blocks rather than rebuilding different bytes under the same
identity.

For an empty manifest, the protected workflow does not invoke `wave-bundle` and creates no bundle,
draft Release, tag, or completion receipt. Lineage may still advance, but there is no package wave to
promote. The standalone command can still encode an empty manifest for local inspection.

`wave-inspect`, `wave-stage`, and `wave-promote` are protected-workflow boundaries, not routine local
release instructions. `wave-stage` mutates GitHub escrow. After prepared escrow is rechecked, the
promotion job exchanges trusted identity for a short-lived NuGet credential and supplies it to
`wave-promote`. Use them only in the protected workflow or an explicitly authorized disposable
rehearsal.

`--clean-room` proves the exact `Sylin.Koan.Templates` nupkg plus FirstUse and GoldenJourney outside
the checkout against only the staged/local package feed. The template gate uses an isolated CLI home,
creates both public template shapes, restores/builds them, and requires a business-visible result from
their console Entity path and web EntityController path. Packing also proves that each nupkg's Koan
dependencies equal the evaluated project graph and that every selected dependency floor names the
selected identity.

The implementation and failure simulations are complete locally, but this cycle has not observed a
real NuGet publication or immutable GitHub Release. Enable immutable Releases and trusted publishing
before the separately authorized first public wave.
