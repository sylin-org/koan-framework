using Xunit;

// Each spec boots a host, and Entity statics resolve through the ambient host; run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
