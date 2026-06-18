[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
// ARCH-0091: one shared factory (container) per engine assembly, injected into spec ctors now that
// IClassFixture is dropped. Replaces the per-spec-class IClassFixture (4 containers) with one.
[assembly: Xunit.AssemblyFixture(typeof(Koan.Data.VectorAdapterSurface.ElasticSearch.Tests.ElasticSearchTestFactory))]
