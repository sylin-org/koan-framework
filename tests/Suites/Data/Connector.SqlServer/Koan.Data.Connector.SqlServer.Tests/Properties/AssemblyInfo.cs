using Xunit;

// These are integration specs: every class sets the global AppHost.Current to its own fixture's
// ServiceProvider and shares a single SQL Server / LocalDB instance. Running classes in parallel stomps
// that global (AdapterNaming.GetOrCompute resolves against a half-swapped provider) and races on shared
// tables. Serialize the whole assembly, matching every other Koan.Data connector suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
