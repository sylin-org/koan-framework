[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
// One pinned Milvus topology serves the executable DAC-58 ledger.
[assembly: Xunit.AssemblyFixture(typeof(Koan.Data.VectorAdapterSurface.Milvus.Tests.MilvusTestFactory))]
