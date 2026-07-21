using Xunit;

// Every spec in this assembly shares one process-global state surface: the in-memory data store keyed by
// "memory://sort-tests" plus the AppHost.Current singleton. xunit runs distinct test classes in parallel by
// default, so concurrent RemoveAll/seed across classes would race (and silently corrupt counts). Serialize
// the assembly — these tests are sub-second and the shared store makes isolation, not throughput, the priority.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
