using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one Redis container shared across the whole assembly (started once before any test,
// disposed after all). Tests run sequentially within the process; cache nodes share one L2 + one
// pub/sub channel, isolating per test via per-test key/tag/channel tokens. Engines parallelize
// across processes (each test project is its own xUnit v3 executable).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(RedisFixture))]
