// The tenancy runtime specs boot a real host and bind it as the process-global AppHost.Current (the repo's
// established ambient-host pattern, mirroring KoanDataSpec). Running test classes in parallel would let one
// test's host-dispose clear AppHost.Current mid-operation in another, so parallelization is disabled here.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
