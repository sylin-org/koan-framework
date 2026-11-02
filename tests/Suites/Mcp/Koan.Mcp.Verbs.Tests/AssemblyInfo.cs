using Xunit;

// Each spec class boots its own Koan host, and the ambient host is process-wide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
