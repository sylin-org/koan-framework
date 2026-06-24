using Xunit;

// Shares the static ambient AppHost + a FakeTimeProvider with the jobs/tenancy machinery; run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
