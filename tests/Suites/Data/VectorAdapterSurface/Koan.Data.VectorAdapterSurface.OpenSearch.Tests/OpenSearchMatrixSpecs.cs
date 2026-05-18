using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.OpenSearch.Tests;

public class OpenSearchSurfaceSpecs : VectorAdapterSurfaceSpecsBase<OpenSearchTestFactory>
{
    public OpenSearchSurfaceSpecs(OpenSearchTestFactory factory) : base(factory) { }
}

public class OpenSearchPartitionSpecs : VectorPartitionSpecsBase<OpenSearchTestFactory>
{
    public OpenSearchPartitionSpecs(OpenSearchTestFactory factory) : base(factory) { }
}

public class OpenSearchSemanticSpecs : VectorSemanticSpecsBase<OpenSearchTestFactory>
{
    public OpenSearchSemanticSpecs(OpenSearchTestFactory factory) : base(factory) { }
}
