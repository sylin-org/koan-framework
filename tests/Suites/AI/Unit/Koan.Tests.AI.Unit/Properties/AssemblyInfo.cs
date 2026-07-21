using Xunit;

// This project mutates process-global state: Koan.AI.Client.With(...) sets the ambient
// AppHost pipeline, AiCategoryScope establishes an ambient routing scope, and
// MediaAnalysisRegistry uses a static registry (reset via ResetForTesting()). Disable
// cross-class parallelization so these globals are not corrupted by concurrent tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
