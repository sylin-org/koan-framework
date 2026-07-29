using Koan.Data.Abstractions;

namespace Koan.Testing;

/// <summary>Stable profile names generated from the Data Adapter Development Primer.</summary>
public static class DataConformanceProfiles
{
    public const string SourceCore = DataClaimProfiles.SourceCore;
    public const string EntityPersistence = DataClaimProfiles.EntityPersistence;
    public const string DeclaredShapeValidation = DataClaimProfiles.DeclaredShapeValidation;
    public const string ManagedStorageLifecycle = DataClaimProfiles.ManagedStorageLifecycle;
    public const string ReadOnlySourceSafety = DataClaimProfiles.ReadOnlySourceSafety;
    public const string ExternalLifecycleSafety = DataClaimProfiles.ExternalLifecycleSafety;
    public const string ExternalDataWriteSafety = DataClaimProfiles.ExternalDataWriteSafety;
    public const string ContainerListing = DataClaimProfiles.ContainerListing;
    public const string ContainerAddressResolution = DataClaimProfiles.ContainerAddressResolution;
    public const string ContainerDescription = DataClaimProfiles.ContainerDescription;
    public const string RecordSampling = DataClaimProfiles.RecordSampling;
    public const string RecordResults = DataClaimProfiles.RecordResults;
    public const string IdentityPlusObjectMapping = DataClaimProfiles.IdentityPlusObjectMapping;
    public const string FlatNameMapping = DataClaimProfiles.FlatNameMapping;
    public const string HybridMapping = DataClaimProfiles.HybridMapping;
    public const string ScalarNestedPathMapping = DataClaimProfiles.ScalarNestedPathMapping;
    public const string SelectiveReadProjection = DataClaimProfiles.SelectiveReadProjection;
    public const string PhysicalProjectionAndIndexing = DataClaimProfiles.PhysicalProjectionAndIndexing;
    public const string RewriteFreeDerivedExpressionIndex = DataClaimProfiles.RewriteFreeDerivedExpressionIndex;
    public const string NativeTtl = DataClaimProfiles.NativeTtl;
    public const string RegisteredReads = DataClaimProfiles.RegisteredReads;
    public const string ProviderBoundedPaging = DataClaimProfiles.ProviderBoundedPaging;
    public const string AtomicBatch = DataClaimProfiles.AtomicBatch;
    public const string ConditionalReplace = DataClaimProfiles.ConditionalReplace;
    public const string Durability = DataClaimProfiles.Durability;
    public const string Isolation = DataClaimProfiles.Isolation;
    public const string ProviderNativeInspection = DataClaimProfiles.ProviderNativeInspection;
    public const string VectorCore = DataClaimProfiles.VectorCore;
    public const string EventualVectorVisibility = DataClaimProfiles.EventualVectorVisibility;
    public const string VectorFilters = DataClaimProfiles.VectorFilters;
    public const string VectorHybrid = DataClaimProfiles.VectorHybrid;
    public const string NamedVectorSpaces = DataClaimProfiles.NamedVectorSpaces;
    public const string VectorContinuation = DataClaimProfiles.VectorContinuation;
    public const string VectorBulk = DataClaimProfiles.VectorBulk;
    public const string VectorAtomicBatch = DataClaimProfiles.VectorAtomicBatch;
    public const string VectorExport = DataClaimProfiles.VectorExport;
    public const string ManagedVectorLifecycle = DataClaimProfiles.ManagedVectorLifecycle;
    public const string VectorIsolation = DataClaimProfiles.VectorIsolation;
    public const string EntityVectorCoordination = DataClaimProfiles.EntityVectorCoordination;
}
