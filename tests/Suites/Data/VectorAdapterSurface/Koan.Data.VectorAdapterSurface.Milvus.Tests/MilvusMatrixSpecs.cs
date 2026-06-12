using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

public class MilvusSurfaceSpecs : VectorAdapterSurfaceSpecsBase<MilvusTestFactory>
{
    public MilvusSurfaceSpecs(MilvusTestFactory factory) : base(factory) { }
}

public class MilvusPartitionSpecs : VectorPartitionSpecsBase<MilvusTestFactory>
{
    public MilvusPartitionSpecs(MilvusTestFactory factory) : base(factory) { }
}

public class MilvusSemanticSpecs : VectorSemanticSpecsBase<MilvusTestFactory>
{
    public MilvusSemanticSpecs(MilvusTestFactory factory) : base(factory) { }
}

public class MilvusFilterConvergenceSpecs : VectorFilterConvergenceSpecsBase<MilvusTestFactory>
{
    public MilvusFilterConvergenceSpecs(MilvusTestFactory factory) : base(factory) { }
}
