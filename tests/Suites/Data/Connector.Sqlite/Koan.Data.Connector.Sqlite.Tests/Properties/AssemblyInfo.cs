using Xunit;

// Specs bind the ambient static AppHost.Current (fixture.BindHost) and seed a shared SQLite store, so
// they must not run in parallel — mirrors the Mongo/Postgres/SqlServer connector suites.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
