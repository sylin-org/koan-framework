# Sylin.Koan.Data.Analytics — technical contract

Owner of the `Sylin.Koan.Data.Analytics` package. Source lives at `src/Koan.Data.Analytics/Koan.Data.Analytics.csproj`; the assembly activates through `AddKoan()` with no manual registration.

## Responsibilities

Declared analytics for Koan entities: named, bounded, self-describing questions with a catalog that humans and agents share. Engine acceleration is elected from a capable connector (DuckDB first, DATA-0123).

## Failure boundary

Unsupported requests reject before provider work with a named capability and a correction. Facts, health, and lock evidence project the composed decision.
