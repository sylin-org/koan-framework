using Xunit;

// Koan hosts bind the process's Entity statics while they run, so the specs that start one run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
