# Contributing to Koan

Koan’s public promise is simple: application code reads as the business, while referenced capabilities own
composition, safe defaults, infrastructure negotiation, runtime explanation, and corrective failure. Contributions
should make that promise smaller, clearer, or more dependable.

## First contribution

1. Use the .NET 10 SDK and choose one affected capability, package, or document owner.
2. Find its current contract through the [documentation curriculum](docs/index.md) and
   [product surface](docs/reference/product-surface.md).
3. Build the smallest affected project and run its focused tests or documentation gate.
4. Update the canonical owner and any package, sample, template, or agent guidance that would
   otherwise teach a different result.
5. Open a focused pull request to `main`, explain the user-visible promise, and sign the commit.

## Before changing code

1. Read `CLAUDE.md` and the current initiative handoff only when your change belongs to an active
   maintainer work item.
2. Map the complete concern before implementation: public contract, owning module, closest existing pattern,
   configuration, runtime facts, health, failure correction, tests, and documentation.
3. Prefer standard .NET hosting, DI, options, health checks, and logging. Add Koan-specific vocabulary only when it
   expresses business intent or removes repeated application ceremony.
4. Put cross-module contracts in genuinely independent abstractions. Keep provider mechanics in connectors and
   policy in the functional owner.

The [engineering workbooks](docs/engineering/README.md) cover repeatable repository tasks. Start with
[test authoring](docs/engineering/test-authoring.md), [adding a connector](docs/engineering/adding-a-connector.md),
or [package versioning](docs/engineering/versioning.md) when those match your change.

## Prove the affected promise

Use the .NET 10 SDK. Build the smallest affected project and run focused tests that exercise the changed contract.
Broaden only when a dependency or shared seam justifies it. A full solution/release ratchet is certification work,
not the default inner loop.

For public examples, include the package reference, host code, configuration, runtime prerequisite, inspection path,
and expected corrective failure. Run the repository’s relevant lint/example checks before proposing the change.

## Keep one public story

- Current guidance starts at the 0.20 preview, `AddKoan()`, the four-line host, and `Entity<T>`.
- The [generated product surface](docs/reference/product-surface.md) owns support maturity and package lines.
- Link to one canonical explanation instead of copying another current guide.
- Keep ADRs, initiatives, assessments, and superseded plans as dated evidence—not required user instructions.
- Update package companions, templates, samples, skills, or feedback guidance when your public change affects them.

## Review and safety

Reviewers look for correctness, a small public surface, explicit application responsibilities, actionable failures,
focused evidence, and documentation that tells the same story as the code. Never commit credentials or private
application material. Use parameterized provider APIs and preserve nullable-reference correctness.

## DCO and licenses

Every commit requires a Developer Certificate of Origin sign-off:

```text
Signed-off-by: Your Name <your.email@example.com>
```

Use `git commit -s` to add it automatically; the full certificate is in `DCO`. Code is Apache-2.0 licensed and
documentation uses [CC BY 4.0](docs/LICENSE-DOCS.md).
