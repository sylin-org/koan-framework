# Local evidence

Use the least evidence that answers the question. Evidence strength depends on the claim.

## Start here

1. Repository instructions and project files.
2. Existing resolved assets and `koan.lock.json`.
3. Relevant application source, configuration, and tests.
4. Existing facts, health, startup output, and logs — `/.well-known/Koan/facts`, `koan://facts`,
   `/health/live`, `/health/ready`, `koan://entities`, `koan://self`.
5. Version-matched framework source, tests, and docs when application evidence does not explain the mechanism.

Once a Koan package is referenced, its own README sits beside the restored package and matches the
version actually in use — prefer it over any other copy when the exact version matters.

Do not run restore, build, startup, tests, or generators to create missing evidence. If the version or runtime state cannot be established from what already exists, label it **Unknown**.

## Match evidence to the claim

- **Capability available:** project and resolved dependency evidence.
- **Configuration intended:** configuration keys and application composition.
- **Provider selected:** existing facts or lock evidence; configuration alone is insufficient.
- **Behavior owned:** application source and focused tests.
- **Runtime result or failure:** captured logs, facts, health, or response evidence.
- **Framework mechanism:** source and tests matching the application's resolved version.

Treat liveness and readiness separately. Explain the contributor that owns a failure and whether the application marks it as required.

## Claim labels

- **Observed:** directly present in a local file or captured output. Cite it.
- **Inferred:** the best explanation connecting observations. State the reasoning and a plausible alternative when relevant.
- **Unknown:** evidence is absent, conflicting, version-mismatched, or would require a mutation to obtain.

Never promote a likely inference to an observation.

## Sensitive evidence

Do not reproduce connection strings, tokens, secrets, private keys, full sensitive claims, or classified Entity fields. Prefer file paths, setting names, provider IDs, and redacted topology.
