namespace Koan.Data.Abstractions;

public sealed class OperationParameterException : ArgumentException
{
    public OperationParameterException(string source, string operation, string correction)
        : base($"Parameters for registered operation '{operation}' on source '{source}' are invalid. {correction}")
    {
        SourceName = source;
        Operation = operation;
        Correction = correction;
    }

    public string SourceName { get; }
    public string Operation { get; }
    public string Correction { get; }
}
