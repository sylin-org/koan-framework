# Sylin.Koan.Jobs.Testing — technical notes

`JobsTestDriver` has friend access to `Koan.Jobs` so the engine owners remain internal. It resolves the
same singleton `JobOrchestrator` and `JobScheduler` that the production worker drives.

The package is xUnit-free and host-agnostic. The application test owns its DI host, `TimeProvider`,
data adapter, and assertions. This keeps deterministic execution faithful to the composed application
instead of introducing a testing runtime beside it.
