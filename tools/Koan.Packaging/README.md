# Koan packaging

Advancing `dev` is the complete package-release instruction. The protected workflow serializes the
event, projects its source delta onto `automation/package-lineage-dev`, automatically mints any
required reverse-dependent identities, proves the exact result, and publishes it. Maintainers do not
select packages or calculate patch versions.

The safe read-only diagnostic is:

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

The packaging-tool invocations are:

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
```

`lineage` intentionally switches a clean checkout to its dedicated local lineage branch and creates
one commit. Run that command only in the protected workflow or a disposable rehearsal checkout. Omit
`--previous-lineage` only when initializing a new lineage. That first projection is an explicit
bootstrap wave: every active package owner receives a fresh identity, preventing an existing package
identity from being rebuilt with different bits or repository metadata. Bootstrap evaluates its
predecessor's package inventory with the pinned toolchain; that predecessor must remain evaluable.
Owners deleted by the source range are recorded as retired at their last calculated identity and do
not produce an artifact. Once bootstrapped, stored identities replace historical version recalculation.

Deleting a package project plus its version owner is complete retirement intent. The compiler removes
it from the active graph, permanently records its final package ID/path/version, and publishes nothing
for that retired owner. A retired ID or path can never return with different bits. Package renames
remain unsupported: introduce a genuinely new ID and path instead, allowing the old owner to retire.

The protected workflow persists that lineage commit, runs the complete public-release green ratchet,
and proves the checkout stayed clean before it invokes `plan` and `pack`.

`SourceCommit` identifies the developer's `dev` event. `VersionCommit` identifies the exact linear
projection that NBGV versions, packing, SourceLink metadata, resumable state, and release evidence use.
The committed lineage state must match the external lineage artifact before planning can begin.
It records every package owner's exact minted identity, so later events compare durable facts instead
of recalculating historical versions with a newer SDK or NBGV tool. A conservative per-package input
map combines known repository/ancestor build policy with evaluated external packed files. Changing a
mapped input selects its package consumers; harmless extra selection is preferred to silently reusing
an identity whose package inputs changed. Known entries remain mapped as deletion tombstones. Deleting
or renaming a new external pack path discovered only by evaluation is currently uncertified;
[PMC-017](../../docs/initiatives/koan-v1/POST-CYCLE-TODO.md#current-register) owns the automated durable-
map or fail-closed contract.

For a long reconciliation rehearsal, add `--resume` to `pack`. Existing artifacts are reused only
after their identity, metadata, symbols policy, and embedded version commit match the manifest.
During a same-source replay, selected identities are packed even when their nupkg is already public so
an interrupted symbol/state reconciliation can reuse the exact version wave safely.

`publish` is protected-workflow machinery. It consumes the verified manifest and exact artifact
directory, obtains its credential from the named environment variable, and records resumable
per-package state.

`--clean-room` proves FirstUse and GoldenJourney outside the checkout against only the staged/local
package feed. Each proof writes separate JSON evidence. Packing also proves that the nupkg's exact
Koan dependencies equal the evaluated project graph and that every selected dependency floor names
the selected identity.
