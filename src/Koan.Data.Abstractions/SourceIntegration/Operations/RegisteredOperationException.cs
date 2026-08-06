namespace Koan.Data.Abstractions;

public sealed class RegisteredOperationException : InvalidOperationException
{
    public RegisteredOperationException(string source, string operation, string correction)
        : base($"Registered operation '{operation}' on source '{source}' cannot execute. {correction}")
    {
        SourceName = source;
        Operation = operation;
        Correction = correction;
    }

    public string SourceName { get; }
    public string Operation { get; }
    public string Correction { get; }
}
