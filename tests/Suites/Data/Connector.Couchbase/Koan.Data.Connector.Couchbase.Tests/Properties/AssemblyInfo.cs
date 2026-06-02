using Xunit;

// Couchbase is a heavyweight container (a full server per test). Running specs in parallel spins up
// multiple Couchbase containers at once, which memory-starves the host and makes their services fail to
// start (a fast ~7s failure), and lets one spec's orphan-cleanup tear down another's live container.
// Serialize the suite, matching every other Koan.Data connector suite.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
