using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.ElasticSearch.Tests;

public class ElasticSearchSurfaceSpecs : VectorAdapterSurfaceSpecsBase<ElasticSearchTestFactory>
{
    public ElasticSearchSurfaceSpecs(ElasticSearchTestFactory factory) : base(factory) { }
}

public class ElasticSearchPartitionSpecs : VectorPartitionSpecsBase<ElasticSearchTestFactory>
{
    public ElasticSearchPartitionSpecs(ElasticSearchTestFactory factory) : base(factory) { }
}

public class ElasticSearchSemanticSpecs : VectorSemanticSpecsBase<ElasticSearchTestFactory>
{
    public ElasticSearchSemanticSpecs(ElasticSearchTestFactory factory) : base(factory) { }
}

public class ElasticSearchFilterConvergenceSpecs : VectorFilterConvergenceSpecsBase<ElasticSearchTestFactory>
{
    public ElasticSearchFilterConvergenceSpecs(ElasticSearchTestFactory factory) : base(factory) { }
}
