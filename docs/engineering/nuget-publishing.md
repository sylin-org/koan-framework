# NuGet publishing

Every push that advances `dev`—direct or merged—is a release event. The
[release-on-dev workflow](../../.github/workflows/release-on-dev.yml) derives the independently
versioned package set from Git, proves the exact artifacts, and publishes them without routine
operator input.

## One-time setup

Configure [nuget.org trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)
for this GitHub repository and the `release-on-dev.yml` workflow, then set the repository Actions
variable `NUGET_USER` to the nuget.org account or
organization that owns the packages. The workflow uses GitHub OIDC to obtain a short-lived API key;
there is no committed or long-lived `NUGET_API_KEY` secret.

If trusted publishing is absent or `NUGET_USER` is empty, publication fails red. It never creates a
tag or reports a successful release without publishing.

## What happens

1. Check out the exact `dev` commit with full history.
2. Build and test the solution with public versioning and high/critical advisory warnings promoted to
   errors.
3. Compile `release-set.json` from NBGV versions at the event's `before` and `after` commits; also
   reconcile current identities missing from nuget.org.
4. Pack only new identities in project-dependency order. Fail on the first pack, metadata, symbols,
   advisory, or dependency-closure error.
5. Copy a small application outside the repository. Restore only the staged `Sylin.Koan.App` and
   SQLite packages, build it, start it, probe `/health`, and perform Entity CRUD over HTTP.
6. Upload the manifest and hashed artifacts as one immutable workflow artifact.
7. Wait for any earlier active `dev` release event, then publish in dependency order. Retry transient
   pushes, wait for nuget.org visibility, and persist `release-state.json` after every identity.
8. Only after convergence, create `release/dev/<12-character-commit>` and attach the manifest/state
   to its GitHub release.

When no identity needs publication, build/test/plan still run and the workflow exits green without a
publish job or tag.

## Happy path

```text
push or merge to dev
```

The workflow summary lists each selected package, previous version, minted version, and reason. A
completed release has all three forms of evidence:

- a green `Release packages from dev` run;
- an attached `release-set.json` whose source commit matches the push;
- a `release/dev/<commit>` release created after registry visibility was confirmed.

No package selection, patch calculation, tag, or credential copy/paste belongs in the happy path.

## Failure → recovery

### Build, test, pack, audit, or clean-room failure

Read the first failed step. Fix the source, metadata, dependency, or advisory and advance `dev` again.
No artifact was published and no release tag was created.

### Publish stopped partway through

Re-run the failed workflow. The manifest and immutable NuGet identities make the operation
idempotent: public identities are marked available, missing identities continue in order, and state is
rewritten as progress is confirmed. Never rebuild a replacement package with the same ID/version.

### A later event is waiting

This is expected. Each push is retained as its own event, and later publication waits for earlier
active events rather than canceling or overtaking them. A completed or failed earlier event no longer
holds the queue.

### nuget.org visibility times out

Re-run after the registry recovers. The compiler queries the flat-container identity before every
push, so a package that became visible after timeout is reconciled safely.

### Trusted-publishing exchange fails

Verify the nuget.org policy names this repository/workflow and that `NUGET_USER` names its owner.
Do not add a long-lived key as a workaround.

## Local diagnosis

```powershell
dotnet run --project tools/Koan.Packaging -- inventory

dotnet run --project tools/Koan.Packaging -- plan `
  --before HEAD~1 --after HEAD `
  --output artifacts/release/release-set.json

dotnet run --project tools/Koan.Packaging -- pack `
  --manifest artifacts/release/release-set.json `
  --output artifacts/release/packages `
  --clean-room
```

Local `publish` exists for controlled recovery/testing and requires an explicitly named environment
credential, but it is not the normal release path:

```powershell
$env:NUGET_API_KEY = '<short-lived credential>'
dotnet run --project tools/Koan.Packaging -- publish `
  --manifest artifacts/release/release-set.json `
  --artifacts artifacts/release/packages `
  --state artifacts/release/release-state.json
```

## Anti-patterns

- Do not skip an individual `dev` release event or batch releases by commit-message convention.
- Do not pack all projects and rely on `--skip-duplicate` as package selection.
- Do not ignore a pack failure and publish the remaining files.
- Do not disable audit, hand-edit the manifest, or publish artifacts from another commit.
- Do not create the release tag before registry convergence.
- Do not publish symbols or nupkgs through a separate loop.

See [versioning.md](versioning.md), [packaging.md](packaging.md), and
[ARCH-0110](../decisions/ARCH-0110-dev-release-compiler.md).
