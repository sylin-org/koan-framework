using Xunit;

// The fixture sets the process-global ASPNETCORE_URLS and binds a real Kestrel port; keep runs serial.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
