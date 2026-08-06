// The conformance spec sets the static AppHost.Current global (VectorAodbConformanceSpecsBase.InitializeAsync) and the
// boot host registers into the process-global DatabaseRouteRegistry. Parallel execution across spec classes would race
// on both; disable it so each spec class runs its full set against a stable AppHost.Current — matching every sibling
// VectorAdapterSurface cell.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
[assembly: Xunit.AssemblyFixture(typeof(Koan.Data.VectorAdapterSurface.SqliteVec.Tests.SqliteVecTestFactory))]
