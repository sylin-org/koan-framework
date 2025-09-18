# Contributing to Koan

Thanks for helping build Koan. This repo favors small, focused PRs with good tests and docs. Start here:

- Read the support docs at `docs/support/` (architecture, adapters, testing, releases, migration).
- Follow the ADR process for meaningful technical decisions (see `docs/decisions`).
- Keep entities POCO and provider-agnostic; push provider specifics into adapters.

Quick checklist:

- Tests: add/adjust unit tests; keep runtime deps minimal.
- Docs: update guides when behavior changes; add examples when useful.
- API surface: prefer additive; mark breaking changes clearly and justify in ADR.

## Local setup

- .NET 9 SDK
- Run: `dotnet build` then `dotnet test` (solution or project-level).

## Code style

- C#: modern features OK; be explicit with nullability.
- Keep public APIs small; use internal helpers.
- Avoid magic strings; prefer enums or constants.

## Review criteria

- Correctness, clarity, and tests.
- Backwards compatibility (when possible).
- Docs: updated appropriately.

## Security

- Don’t include credentials in tests/samples.
- Parameterize SQL; avoid string interpolation in adapters.

## DCO sign-off

This project uses the Developer Certificate of Origin (DCO). Add a Signed-off-by line to each commit:

Signed-off-by: Your Name <your.email@example.com>

You can use `git commit -s` to add it automatically. See the `DCO` file for the certificate text.

## License notes

- Code is Apache-2.0 licensed (`LICENSE`).
- Documentation is CC BY 4.0 (`docs/LICENSE-DOCS.md`).

## Where to go next

- `docs/support/README.md` - overview and deep dives.
