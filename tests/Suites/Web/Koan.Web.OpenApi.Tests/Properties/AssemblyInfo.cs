using Xunit;

// ARCH-0091: the fixture binds the process-global AppHost.Current, so collections run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
