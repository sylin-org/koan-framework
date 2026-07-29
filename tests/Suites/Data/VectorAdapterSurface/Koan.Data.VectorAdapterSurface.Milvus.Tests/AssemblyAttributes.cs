[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
// One pinned Milvus topology serves the provider suite.
[assembly: Xunit.AssemblyFixture(typeof(Koan.Data.VectorAdapterSurface.Milvus.Tests.MilvusTestFactory))]
