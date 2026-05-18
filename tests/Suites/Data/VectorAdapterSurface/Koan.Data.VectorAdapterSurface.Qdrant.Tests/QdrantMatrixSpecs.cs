using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

public class QdrantSurfaceSpecs : VectorAdapterSurfaceSpecsBase<QdrantTestFactory>
{
    public QdrantSurfaceSpecs(QdrantTestFactory factory) : base(factory) { }
}

public class QdrantPartitionSpecs : VectorPartitionSpecsBase<QdrantTestFactory>
{
    public QdrantPartitionSpecs(QdrantTestFactory factory) : base(factory) { }
}

public class QdrantSemanticSpecs : VectorSemanticSpecsBase<QdrantTestFactory>
{
    public QdrantSemanticSpecs(QdrantTestFactory factory) : base(factory) { }
}
