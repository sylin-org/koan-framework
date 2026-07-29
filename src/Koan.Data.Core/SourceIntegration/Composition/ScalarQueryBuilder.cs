namespace Koan.Data.Core;

public sealed class ScalarQueryBuilder : OperationBuilderBase<ScalarQueryBuilder>
{
    internal ScalarQueryBuilder(string source, string name, Type scalarType)
        : base(source, name, Koan.Data.Abstractions.OperationResultKind.Scalar, scalarType) { }
}
