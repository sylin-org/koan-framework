using Xunit;

// ARCH-0091: BuildRegistry() seeds the process-global AssemblyCache.Instance, so collections run
// serially to keep that shared static deterministic across the assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
