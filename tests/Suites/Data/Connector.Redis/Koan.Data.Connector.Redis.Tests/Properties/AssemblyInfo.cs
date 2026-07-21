using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one Redis container shared across the whole assembly (started once before any test,
// disposed after all). Tests run sequentially within the process because the static Entity<T> API
// resolves through the process-global AppHost.Current; engines parallelize across processes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(RedisFixture))]
