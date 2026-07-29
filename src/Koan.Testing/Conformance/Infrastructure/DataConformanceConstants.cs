namespace Koan.Testing.Conformance.Infrastructure;

internal static class DataConformanceConstants
{
    public const int SchemaVersion = 1;
    public const int ExpectedCellCount = 105;
    public const int ExpectedProfileCount = 39;
    public const string ProtocolVersion = "data-adapter-conformance/1";
    public const string CatalogResourceSuffix = "data-conformance-catalog.json";
    public const string AcceptanceTrait = "KoanAcceptanceId";
    public const string CategoryTrait = "Category";
    public const string Category = "DataConformance";
    public const string DefaultCase = "default";
    public const string FrameworkOwner = "Framework";
    public const string AdapterOwner = "Adapter";
    public const string VectorKnnCapability = "vector.knn";
    public const string VectorFiltersCapability = "vector.filters";
    public const string VectorHybridCapability = "vector.hybrid";
    public const string VectorNativeContinuationCapability = "vector.nativeContinuation";
    public const string VectorStreamingResultsCapability = "vector.streamingResults";
    public const string VectorMultiVectorPerEntityCapability = "vector.multiVectorPerEntity";
    public const string VectorBulkUpsertCapability = "vector.bulkUpsert";
    public const string VectorBulkDeleteCapability = "vector.bulkDelete";
    public const string VectorAtomicBatchCapability = "vector.atomicBatch";
    public const string VectorScoreNormalizationCapability = "vector.scoreNormalization";
    public const string VectorDynamicCollectionsCapability = "vector.dynamicCollections";
}
