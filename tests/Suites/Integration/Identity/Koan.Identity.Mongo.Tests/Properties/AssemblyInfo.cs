using Koan.Testing.Containers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AssemblyFixture(typeof(MongoFixture))]
