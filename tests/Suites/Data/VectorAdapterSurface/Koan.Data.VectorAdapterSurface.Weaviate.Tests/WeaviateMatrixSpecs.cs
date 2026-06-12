using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

public class WeaviateSurfaceSpecs : VectorAdapterSurfaceSpecsBase<WeaviateTestFactory>
{
    public WeaviateSurfaceSpecs(WeaviateTestFactory factory) : base(factory) { }
}

public class WeaviatePartitionSpecs : VectorPartitionSpecsBase<WeaviateTestFactory>
{
    public WeaviatePartitionSpecs(WeaviateTestFactory factory) : base(factory) { }
}

public class WeaviateSemanticSpecs : VectorSemanticSpecsBase<WeaviateTestFactory>
{
    public WeaviateSemanticSpecs(WeaviateTestFactory factory) : base(factory) { }
}

public class WeaviateFilterConvergenceSpecs : VectorFilterConvergenceSpecsBase<WeaviateTestFactory>
{
    public WeaviateFilterConvergenceSpecs(WeaviateTestFactory factory) : base(factory) { }
}
