using Xunit;

// Keep declaration-facade specs sequential so their flow-scoped composition setup and failure diagnostics remain
// deterministic. Each catalog is host-owned; this scheduling rule is not a runtime ownership requirement.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
