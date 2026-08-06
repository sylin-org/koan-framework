namespace Koan.Data.Core;

public sealed class RecordQueryBuilder : OperationBuilderBase<RecordQueryBuilder>
{
    internal RecordQueryBuilder(string source, string name)
        : base(source, name, Koan.Data.Abstractions.OperationResultKind.Records, null) { }

    public RecordQueryBuilder MaxRecords(int value) { SetMaxRecords(value); return this; }
    public RecordQueryBuilder MaxBytes(long value) { SetMaxBytes(value); return this; }
}
