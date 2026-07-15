# NuGet publishing

Every push that advances `dev`—direct or merged—is a release event. The
[release-on-dev workflow](../../.github/workflows/release-on-dev.yml) derives independently versioned
packages from Git, proves the exact artifacts, and publishes without routine operator input.

## One-time setup

Configure [nuget.org trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
for this GitHub repository and `release-on-dev.yml`, then set the repository Actions variable
`NUGET_USER` to the nuget.org package owner. GitHub OIDC produces a short-lived API key; there is no
committed or long-lived publishing secret.

If trusted publishing is absent or `NUGET_USER` is empty, publication fails red. It never creates a
release tag or reports success without registry convergence.

## What happens

1. Check out the `dev` source event with full history, then wait for every earlier requested, queued,
   waiting, pending, or running release event. Serialization happens before version calculation.
2. Fetch the prior `automation/package-lineage-dev` tip. The compiler applies the exact prior-source
   to current-source tree delta onto that linear version history.
3. Bootstrap every owner once, or detect breaking `version.json` tiers and evaluated shared-input
   consumers; write markers only for affected owners that would otherwise retain their prior identity.
4. Commit and push the exact `VersionCommit`. Its state records every package/version identity;
   `release-lineage.json` records it separately from the developer's `SourceCommit`.
5. Run the complete green ratchet at that version commit with the repository-pinned SDK, public
   versioning, high/critical advisory warnings as errors, and a clean tracked-tree assertion.
6. Compile `release-set.json` from `PreviousVersionCommit` to `VersionCommit`, verify it against the
   committed lineage state, and reconcile current identities missing from nuget.org.
7. Pack every selected identity in dependency order, inspect metadata/symbols/ranges/hashes, and run
   FirstUse plus GoldenJourney outside the checkout against only the staged package feed.
8. Publish in dependency order. Existing nupkgs are reconciled rather than replaced; symbols replay,
   transient pushes retry, registry visibility is awaited, and `release-state.json` advances after
   each complete identity.
9. Only after convergence, create or verify `release/dev/<12-character-source-commit>` as a
   non-forced tag at the exact version commit, then attach lineage, manifest, and state evidence.

When no identity needs publication, lineage/build/test/plan still run and the workflow exits green
without publishing or tagging.

## Happy path

```text
push or merge to dev
```

The workflow summary lists the source/version commits, breaking closure, generated markers, and each
selected package's previous version, resulting version, and reason. A completed release has:

- a green `Release packages from dev` run;
- attached `release-lineage.json` and `release-set.json` that distinguish developer source from
  shipped version truth; and
- a `release/dev/<source-commit>` release targeted at the version commit after registry visibility
  was confirmed.

No package selection, patch calculation, tag, or credential copy/paste belongs in the happy path.

## Failure → recovery

### Build, test, pack, audit, or clean-room failure

Read the first failed step. Fix the source, metadata, dependency, or advisory and advance `dev` again.
No package artifact was published and no release tag was created. The compiled version lineage remains
durable; the later source event advances from it and registry reconciliation closes unpublished gaps.

### Publish stopped partway through

Re-run the failed workflow before a later lineage event advances. The same source event resolves to
the same durable version commit. Every selected artifact is reproduced and verified; public nupkgs
are reconciled (including symbol replay), missing identities continue in dependency order, and state
advances only after each identity is available. Never build different bits under the same ID/version.

If a later lineage event has already advanced after a partial nupkg/symbol publication, the current
workflow does not yet certify cross-event artifact recovery. Treat that as red operational debt; do
not claim success from nupkg visibility alone or hand-pack a replacement. The bounded successor is
tracked in the post-cycle register.

### A later event is waiting

This is expected. Each push remains its own event, and the entire lineage/release operation waits for
earlier active events rather than canceling or overtaking them. A completed or failed earlier event no
longer holds the queue; a later source contains the earlier source delta and advances from the last
durable lineage tip.

### nuget.org visibility times out

Re-run after the registry recovers. The compiler queries the registry before publication, and publish
checks again before every push, so an identity that became visible after timeout is reconciled safely.

### Trusted-publishing exchange fails

Verify the nuget.org policy names this repository/workflow and that `NUGET_USER` names its owner. Do
not add a long-lived key as a workaround.

## Local diagnosis

```powershell
dotnet run --project tools/Koan.Packaging -- inventory
```

`inventory` is read-only. `lineage` intentionally creates and switches branches, so reproduce the
workflow only in a clean disposable checkout using the
[packaging tool sequence](../../tools/Koan.Packaging/README.md).

Local `publish` exists for controlled recovery/testing and requires an explicitly named short-lived
credential, but it is not the normal release path.

## Anti-patterns

- Do not skip an individual `dev` release event or batch releases by commit-message convention.
- Do not pack every repository package and call that selection; pack the exact compiled set.
- Do not ignore a pack failure and publish the remaining files.
- Do not disable audit, edit lineage/manifest evidence, or publish artifacts from another commit.
- Do not create the release tag before registry convergence.
- Do not calculate versions before the release queue lease or create merge commits on the linear
  package-lineage branch.
- Do not treat `SourceCommit` as package metadata; `VersionCommit` owns the shipped bits.

See [versioning.md](versioning.md), [packaging.md](packaging.md), and
[ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).
