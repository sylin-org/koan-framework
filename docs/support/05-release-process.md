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
