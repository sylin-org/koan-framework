using Xunit;

// Entity<T> resolves through the process-wide AppHost, so catalog host tests are sequential.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
