using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one SQLite store shared across the assembly; tests run sequentially (the static Entity<T> API resolves
// through the process-global AppHost.Current). Engines parallelize across processes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(SqliteFixture))]
