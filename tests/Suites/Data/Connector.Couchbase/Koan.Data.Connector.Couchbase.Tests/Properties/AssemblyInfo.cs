using Koan.Testing.Containers;
using Xunit;

// ARCH-0091: one Couchbase container shared across the whole assembly (started once before any test,
// disposed after all). Couchbase is a heavyweight container (a full server node), so an assembly-shared
// fixture is doubly important. Tests run sequentially within the process because the static Entity<T> API
// resolves through the process-global AppHost.Current; engines parallelize across processes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(CouchbaseFixture))]
