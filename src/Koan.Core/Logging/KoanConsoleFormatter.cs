using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Koan.Core.Logging;

internal sealed class KoanConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "KoanConsole";

    private readonly IDisposable? _optionsReloadToken;
    private KoanConsoleFormatterOptions _options;

    public KoanConsoleFormatter(IOptionsMonitor<KoanConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.CurrentValue;
        _optionsReloadToken = options.OnChange(updated => _options = updated);
    }

    public void Dispose()
        => _optionsReloadToken?.Dispose();

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        if (textWriter == null)
        {
            throw new ArgumentNullException(nameof(textWriter));
        }

        var opts = _options;
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);

        if (string.IsNullOrEmpty(message) && logEntry.Exception == null)
        {
            return;
        }

        var builder = new StringBuilder();

        var timestamp = GetTimestamp(opts);
        if (!string.IsNullOrEmpty(timestamp))
        {
            builder.Append(timestamp);
            builder.Append(' ');
        }

        builder.Append(GetLogLevelString(logEntry.LogLevel));

        if (opts.IncludeThreadId)
        {
            builder.Append('|');
            builder.Append('#');
            builder.Append(Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrEmpty(message))
        {
            builder.Append('|');
            builder.Append(message);
        }

        if (opts.IncludeSourceSuffix)
        {
            var source = FormatCategory(logEntry.Category, opts);
            if (!string.IsNullOrEmpty(source))
            {
                builder.Append(' ');
                builder.Append('(');
                builder.Append(source);
                builder.Append(')');
            }
        }

        textWriter.WriteLine(builder.ToString());

        if (logEntry.Exception != null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }

        if (opts.IncludeScopes && scopeProvider != null)
        {
            scopeProvider.ForEachScope((scope, state) =>
            {
                state.WriteLine($" => {scope}");
            }, textWriter);
        }
    }

    private static string GetLogLevelString(LogLevel level)
        => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            LogLevel.None => "none",
            _ => level.ToString().ToLowerInvariant()
        };

    private static string? GetTimestamp(KoanConsoleFormatterOptions options)
    {
        if (string.IsNullOrEmpty(options.TimestampFormat))
        {
            return null;
        }

        var now = options.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        return now.ToString(options.TimestampFormat, CultureInfo.InvariantCulture);
    }

    private static string? FormatCategory(string? category, KoanConsoleFormatterOptions options)
    {
        if (!options.IncludeCategory || string.IsNullOrEmpty(category))
        {
            return null;
        }

        return options.CategoryMode switch
        {
            KoanConsoleCategoryMode.Full => category,
            KoanConsoleCategoryMode.Namespace => TrimToNamespace(category),
            _ => TrimToTypeName(category)
        };
    }

    private static string TrimToTypeName(string category)
    {
        var index = category.LastIndexOf('.');
        if (index >= 0 && index < category.Length - 1)
        {
            return category[(index + 1)..];
        }

        return category;
    }

    private static string TrimToNamespace(string category)
    {
        var segments = category.Split('.');
        if (segments.Length <= 2)
        {
            return category;
        }

        return string.Join('.', segments.Take(segments.Length - 1));
    }
}

internal sealed class KoanConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool IncludeCategory { get; set; } = true;
    public KoanConsoleCategoryMode CategoryMode { get; set; } = KoanConsoleCategoryMode.Short;
    public bool IncludeSourceSuffix { get; set; } = true;
    public bool IncludeThreadId { get; set; }
}

internal enum KoanConsoleCategoryMode
{
    Full,
    Namespace,
    Short
}
