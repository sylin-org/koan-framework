# Sylin.Koan.Data.Hygiene — technical contract

Owner of the `Sylin.Koan.Data.Hygiene` package. Source lives at `src/Koan.Data.Hygiene/Koan.Data.Hygiene.csproj`; the assembly activates through `AddKoan()` with no manual registration.

## Responsibilities

Declarative field hygiene for Koan Entities - [Trim], [Lowercase] and [Uppercase] normalize annotated string properties on the persisted clone across every write path.

## Failure boundary

Unsupported requests reject before provider work with a named capability and a correction. Facts, health, and lock evidence project the composed decision.
