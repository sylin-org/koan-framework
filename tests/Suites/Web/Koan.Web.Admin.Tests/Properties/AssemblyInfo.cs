using Xunit;

// Each real host binds Koan's process-global application host, so these facts run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
