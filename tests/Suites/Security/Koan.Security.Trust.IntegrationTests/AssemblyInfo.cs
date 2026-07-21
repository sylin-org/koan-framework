using Xunit;

// ARCH-0091: the AuthE2EFixture boots a Koan web host that binds the process-global AppHost.Current.
// Running test collections in parallel would let concurrent boots race on that shared static, so the
// whole assembly runs serially (one TestServer at a time).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
