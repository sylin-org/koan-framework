using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.InMemory.Tests;

public class InMemoryVectorSurfaceSpecs : VectorAdapterSurfaceSpecsBase<InMemoryVectorTestFactory>
{
    public InMemoryVectorSurfaceSpecs(InMemoryVectorTestFactory factory) : base(factory) { }
}

public class InMemoryVectorPartitionSpecs : VectorPartitionSpecsBase<InMemoryVectorTestFactory>
{
    public InMemoryVectorPartitionSpecs(InMemoryVectorTestFactory factory) : base(factory) { }
}

public class InMemoryVectorSemanticSpecs : VectorSemanticSpecsBase<InMemoryVectorTestFactory>
{
    public InMemoryVectorSemanticSpecs(InMemoryVectorTestFactory factory) : base(factory) { }
}
