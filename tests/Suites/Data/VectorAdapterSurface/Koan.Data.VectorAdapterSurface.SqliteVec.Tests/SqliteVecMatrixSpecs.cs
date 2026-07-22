using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.SqliteVec.Tests;

public sealed class SqliteVecSurfaceSpecs(SqliteVecTestFactory factory)
    : VectorAdapterSurfaceSpecsBase<SqliteVecTestFactory>(factory);

public sealed class SqliteVecPartitionSpecs(SqliteVecTestFactory factory)
    : VectorPartitionSpecsBase<SqliteVecTestFactory>(factory);

public sealed class SqliteVecSemanticSpecs(SqliteVecTestFactory factory)
    : VectorSemanticSpecsBase<SqliteVecTestFactory>(factory);

public sealed class SqliteVecFilterConvergenceSpecs(SqliteVecTestFactory factory)
    : VectorFilterConvergenceSpecsBase<SqliteVecTestFactory>(factory);
