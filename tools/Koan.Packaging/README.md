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
bootstrap wave: every package owner receives a fresh identity, preventing an existing package identity
from being rebuilt with different bits or repository metadata.

The protected workflow persists that lineage commit, runs the complete public-release green ratchet,
and proves the checkout stayed clean before it invokes `plan` and `pack`.

`SourceCommit` identifies the developer's `dev` event. `VersionCommit` identifies the exact linear
projection that NBGV versions, packing, SourceLink metadata, resumable state, and release evidence use.
The committed lineage state must match the external lineage artifact before planning can begin.
It records every package owner's exact minted identity, so later events compare durable facts instead
of recalculating historical versions with a newer SDK or NBGV tool.
Repository-wide build policy, ancestor `Directory.*` policy, and external packed files are evaluated
as shared package inputs. Changing one automatically selects only its evaluated package consumers.

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
