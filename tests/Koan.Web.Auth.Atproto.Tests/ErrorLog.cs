using Koan.Web.Auth.Connector.Atproto.Storage;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>The errors, warnings and information a session store logs, kept for a spec to read.</summary>
internal sealed class ErrorLog : ILogger<AtprotoSessionStore>
{
    private readonly List<string> errors = [];
    private readonly List<string> warnings = [];
    private readonly List<string> information = [];

    public IReadOnlyList<string> Errors
    {
        get { lock (errors) return errors.ToArray(); }
    }

    public IReadOnlyList<string> Warnings
    {
        get { lock (errors) return warnings.ToArray(); }
    }

    public IReadOnlyList<string> Information
    {
        get { lock (errors) return information.ToArray(); }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (errors)
        {
            if (logLevel >= LogLevel.Error) errors.Add(formatter(state, exception));
            else if (logLevel == LogLevel.Warning) warnings.Add(formatter(state, exception));
            else if (logLevel == LogLevel.Information) information.Add(formatter(state, exception));
        }
    }
}
