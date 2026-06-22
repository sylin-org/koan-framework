// The integration tests boot real hosts that set the process-global AppHost.Current and share the static
// classification registries; serialize the whole assembly so hosts never race.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
