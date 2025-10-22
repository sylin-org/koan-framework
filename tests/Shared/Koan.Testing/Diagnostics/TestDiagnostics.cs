using System.Text.Json.Serialization;
using Koan.Testing.Contracts;
using Xunit.Abstractions;

namespace Koan.Testing.Diagnostics;

public sealed class TestDiagnostics : ITestDiagnostics
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ITestOutputHelper _output;
    private readonly string _suite;
    private readonly string _spec;
    private readonly object _lock = new();

    public TestDiagnostics(ITestOutputHelper output, string suite, string spec)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _suite = suite ?? throw new ArgumentNullException(nameof(suite));
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    public void Info(string message, object? data = null) => Write("info", message, data, null);

    public void Debug(string message, object? data = null) => Write("debug", message, data, null);

    public void Warn(string message, object? data = null) => Write("warn", message, data, null);

    public void Error(string message, object? data = null, Exception? exception = null) => Write("error", message, data, exception);

    public IDisposable BeginScope(string name, object? data = null)
    {
        Info($"⟪{name}⟫ start", data);
        return new Scope(this, name, data);
    }

    private void Write(string level, string message, object? data, Exception? exception)
    {
        var payload = new
        {
            ts = DateTimeOffset.UtcNow.ToString("O"),
            level,
            suite = _suite,
            spec = _spec,
            message,
            data,
            exception = exception?.ToString()
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        lock (_lock)
        {
            _output.WriteLine(json);
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly TestDiagnostics _owner;
        private readonly string _name;
        private readonly object? _data;
        private bool _disposed;

        public Scope(TestDiagnostics owner, string name, object? data)
        {
            _owner = owner;
            _name = name;
            _data = data;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Info($"⟪{_name}⟫ stop", _data);
        }
    }
}
