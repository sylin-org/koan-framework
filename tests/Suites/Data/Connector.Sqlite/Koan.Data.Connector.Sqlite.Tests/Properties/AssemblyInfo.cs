using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one SQLite store shared across the whole assembly (a unique temp-file database created once
// before any test, deleted after all). Tests run sequentially within the process because the static
// Entity<T> API resolves through the process-global AppHost.Current; engines parallelize across processes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(SqliteFixture))]
