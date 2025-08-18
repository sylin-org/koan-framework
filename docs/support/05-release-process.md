# Release Process

- Versioning: semantic versioning across packages.
- Changelog: maintain per release in root CHANGELOG.md.
- Package publishing: publish provider packages independently as needed.
- Docs: update guides and ADRs when behavior changes.

## Steps
1) Ensure main is green (build+tests).
2) Update CHANGELOG.md.
3) Tag the commit (e.g., v0.1.0) and push tag.
4) Publish NuGet packages.

## CI note: Git history depth
We use Nerdbank.GitVersioning (NBGV). CI must check out with full history so version height can be computed. Our workflows set:

- actions/checkout@v4 with `fetch-depth: 0` and `fetch-tags: true`.

If you see errors like "Shallow clone lacks the objects required to calculate version height" in CI, ensure the checkout step includes those options.
