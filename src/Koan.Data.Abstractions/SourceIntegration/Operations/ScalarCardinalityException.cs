namespace Koan.Data.Abstractions;

public sealed class ScalarCardinalityException : InvalidOperationException
{
    public ScalarCardinalityException(string operation, int records, int fields)
        : base($"Scalar operation '{operation}' returned {records} record(s) and {fields} field(s); exactly one value is required.")
    {
        Operation = operation;
        Records = records;
        Fields = fields;
    }

    public string Operation { get; }
    public int Records { get; }
    public int Fields { get; }
}
