using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace Sora.Core.Logging;

public class SoraLogFormatter : ConsoleFormatter
{
    private static readonly Regex SoraContextPattern = new(@"^\[(sora:[^]]+|flow:[^]]+)\]", RegexOptions.Compiled);
    private const int ContextColumnWidth = 15;
    private readonly SoraLogContextRegistry? _contextRegistry;
    
    public SoraLogFormatter() : base("sora") { }
    
    public SoraLogFormatter(SoraLogContextRegistry contextRegistry) : base("sora") 
    {
        _contextRegistry = contextRegistry;
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? string.Empty;
        
        // Try to find registered context first (preferred approach)
        var registeredContext = _contextRegistry?.FindContext(message, logEntry.Category);
        if (registeredContext != null)
        {
            FormatWithRegisteredContext(logEntry, message, registeredContext, textWriter);
        }
        // Check if this is already a Sora-formatted message with context tags
        else if (SoraContextPattern.Match(message) is { Success: true } match)
        {
            FormatSoraMessage(logEntry, message, match, textWriter);
        }
        else
        {
            // Fall back to standard formatting using category-based context
            FormatStandardMessage(logEntry, message, textWriter);
        }
    }

    private static void FormatWithRegisteredContext<TState>(in LogEntry<TState> logEntry, string message, SoraLogContext context, TextWriter textWriter)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var levelChar = GetLogLevelChar(logEntry.LogLevel);
        
        // Use the context to clean the message (no hardcoded replacements)
        var cleanMessage = context.CleanMessage(message);
        var paddedContext = context.DisplayName.PadRight(ContextColumnWidth);
        
        textWriter.WriteLine($"│ {levelChar} {timestamp} {paddedContext} {cleanMessage}");
    }

    private static void FormatSoraMessage<TState>(in LogEntry<TState> logEntry, string message, Match contextMatch, TextWriter textWriter)
    {
        var timestamp = logEntry.EventId.Id != 0 ? 
            DateTime.Now.ToString("HH:mm:ss") : 
            DateTime.Now.ToString("HH:mm:ss");
            
        var levelChar = GetLogLevelChar(logEntry.LogLevel);
        var context = contextMatch.Groups[1].Value;
        var actualMessage = message.Substring(contextMatch.Length).TrimStart();
        
        var paddedContext = context.PadRight(ContextColumnWidth);
        textWriter.WriteLine($"│ {levelChar} {timestamp} {paddedContext} {actualMessage}");
    }

    private static void FormatStandardMessage<TState>(in LogEntry<TState> logEntry, string message, TextWriter textWriter)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var levelChar = GetLogLevelChar(logEntry.LogLevel);
        var context = GetCategoryContext(logEntry.Category).PadRight(ContextColumnWidth);
        
        textWriter.WriteLine($"│ {levelChar} {timestamp} {context} {message}");
    }

    private static string GetCategoryContext(string category)
    {
        // Simplified fallback context mapping for categories not registered in the registry
        // Most contexts should be handled by the registry system instead
        return category switch
        {
            var c when c.Contains("FlowOrchestrator") => "Flow Workers",
            var c when c.Contains("BackgroundService") => "Background Services", 
            var c when c.Contains("StartupProbe") => "Initialization",
            var c when c.Contains("HealthProbe") => "Health Checks",
            var c when c.Contains("Messaging") => "Messaging",
            var c when c.Contains("Data") => "Data Access",
            var c when c.Contains("Microsoft.AspNetCore") => "HTTP Server",
            _ => "Runtime"
        };
    }

    private static string GetLogLevelChar(LogLevel level) => level switch
    {
        LogLevel.Error => "E",
        LogLevel.Warning => "W",
        LogLevel.Information => "I", 
        LogLevel.Debug => "D",
        LogLevel.Trace => "T",
        _ => "I"
    };
}