using Xunit;

// Jobs specs share the static ambient AppHost + a FakeTimeProvider; run them serially (matches the Data suites).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
