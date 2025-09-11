using Microsoft.Extensions.Logging;

namespace Sora.Core.Logging;

/// <summary>
/// Represents a logging context that modules can register to achieve semantic, consistent log formatting.
/// This follows Sora's auto-registration pattern and separation of concerns principle.
/// </summary>
public sealed class SoraLogContext
{
    public string Name { get; }
    public string DisplayName { get; }
    public LogLevel MinimumLevel { get; }
    public string[]? MessagePrefixes { get; }
    public string[]? CategoryPatterns { get; }

    public SoraLogContext(
        string name, 
        string displayName, 
        LogLevel minimumLevel = LogLevel.Information,
        string[]? messagePrefixes = null,
        string[]? categoryPatterns = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        MinimumLevel = minimumLevel;
        MessagePrefixes = messagePrefixes;
        CategoryPatterns = categoryPatterns;
    }

    /// <summary>
    /// Creates a scoped logger that automatically applies this context.
    /// This provides a semantic API that eliminates the need for manual prefixing.
    /// </summary>
    public ILogger CreateScopedLogger(ILogger baseLogger)
    {
        return new SoraContextLogger(baseLogger, this);
    }

    /// <summary>
    /// Determines if this context matches a given log entry based on message content or category.
    /// </summary>
    internal bool Matches(string message, string category)
    {
        // Check message prefixes
        if (MessagePrefixes != null)
        {
            foreach (var prefix in MessagePrefixes)
            {
                if (message.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Check category patterns
        if (CategoryPatterns != null)
        {
            foreach (var pattern in CategoryPatterns)
            {
                if (category.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cleans message content by removing registered prefixes.
    /// This eliminates the need for hardcoded string replacements in the formatter.
    /// </summary>
    internal string CleanMessage(string message)
    {
        if (MessagePrefixes == null) return message;

        var cleanMessage = message;
        foreach (var prefix in MessagePrefixes)
        {
            cleanMessage = cleanMessage.Replace(prefix, "", StringComparison.OrdinalIgnoreCase);
        }

        return cleanMessage.TrimStart();
    }
}

/// <summary>
/// A logger wrapper that automatically applies context information to log entries.
/// This provides the semantic developer experience Sora aims for.
/// </summary>
internal sealed class SoraContextLogger : ILogger
{
    private readonly ILogger _baseLogger;
    private readonly SoraLogContext _context;

    public SoraContextLogger(ILogger baseLogger, SoraLogContext context)
    {
        _baseLogger = baseLogger ?? throw new ArgumentNullException(nameof(baseLogger));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _baseLogger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) 
        => logLevel >= _context.MinimumLevel && _baseLogger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // Prefix the message with context information in a standard format
        var originalMessage = formatter(state, exception);
        var contextualMessage = $"[{_context.Name}] {originalMessage}";

        _baseLogger.Log(logLevel, eventId, contextualMessage, exception, (msg, ex) => msg);
    }
}