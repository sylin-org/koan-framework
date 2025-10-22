namespace Koan.Testing.Diagnostics;

public interface ITestDiagnostics
{
    void Info(string message, object? data = null);

    void Debug(string message, object? data = null);

    void Warn(string message, object? data = null);

    void Error(string message, object? data = null, Exception? exception = null);

    IDisposable BeginScope(string name, object? data = null);
}
