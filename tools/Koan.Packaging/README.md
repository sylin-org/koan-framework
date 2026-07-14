# Koan packaging

The release compiler turns an advancement of `dev` into independently versioned NuGet artifacts.
It derives package impact from NBGV at the two Git endpoints; maintainers do not select packages or
calculate versions.

```powershell
dotnet run --project tools/Koan.Packaging -- plan --before <before-sha> --after <after-sha> --output artifacts/release/release-set.json
dotnet run --project tools/Koan.Packaging -- pack --manifest artifacts/release/release-set.json --output artifacts/release/packages --clean-room
```

For a long local reconciliation rehearsal, add `--resume`. Existing artifacts are reused only after
their identity, metadata, symbols policy, and embedded repository commit match the manifest. The
protected workflow always starts from an empty artifact directory.

`publish` is intended for the protected GitHub workflow. It consumes the verified manifest and exact
artifact directory, obtains its credential from the named environment variable, and records resumable
per-package state.

`--clean-room` proves two applications outside the checkout against only the staged/local-feed
package closure: FirstUse preserves the shortest meaningful result, and GoldenJourney proves that
the same composition model survives business rules, durable jobs, bounded agent collaboration, and
an explained adapter rejection/recovery cycle. Each proof writes a separate JSON evidence file.
