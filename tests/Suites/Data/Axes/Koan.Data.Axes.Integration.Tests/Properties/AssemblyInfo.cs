using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one SQLite store shared across the assembly; specs run sequentially (the static Entity<T> API resolves
// through the process-global AppHost.Current). The discoverable [DataAxis] types (ArchivedAxis, RegionAxis) are
// AppliesTo-gated to their own marker attributes, so each spec sees only the axes its entities opt into.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(SqliteFixture))]
