# Seed Packs

Seed packs capture deterministic datasets that specs can rely on during Arrange and Assert
phases. Every file is versioned JSON (or NDJSON) and referenced via a stable identifier such as
`core/default`.

Guidelines:

- Keep packs small and explicit—prefer named samples over anonymous arrays.
- When you update a pack, bump the `revision` property (if present) and describe changes in the
  suite changelog.
- Specs access packs via `SeedPackFixture` to ensure consistent parsing and diagnostics.
