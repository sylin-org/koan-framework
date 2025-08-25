using Xunit;

// Disable parallel test execution within this assembly to avoid cross-factory
// configuration interference (shared static caches and overlapping server spins).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
