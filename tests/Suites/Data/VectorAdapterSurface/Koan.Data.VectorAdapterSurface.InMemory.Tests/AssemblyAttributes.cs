// Spec classes in this assembly share the static AppHost.Current global as a fallback for tests
// where AsyncLocal flow doesn't reach (xUnit IClassFixture + IAsyncLifetime quirks). Parallel
// execution across spec classes would race on that global; disable it so each spec class runs
// its full set of tests against a stable AppHost.Current pointing at its own factory.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
// ARCH-0091: one shared factory per engine assembly, injected into spec ctors now that
// IClassFixture is dropped.
[assembly: Xunit.AssemblyFixture(typeof(Koan.Data.VectorAdapterSurface.InMemory.Tests.InMemoryVectorTestFactory))]
