using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Koan.Core.Logging;

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddKoanLogging(this IServiceCollection services)
    {
        // Add the context registry system first
        services.AddKoanLoggingContexts();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole(options =>
            {
                options.FormatterName = "Koan";
                // When standard output carries a protocol, diagnostics belong on stderr.
                // The channel is process-derived, so this is already correct no matter when options
                // materialize.
                options.LogToStandardErrorThreshold =
                    Koan.Core.Hosting.KoanStandardStreams.IsStandardOutputProtocol
                        ? LogLevel.Trace
                        : LogLevel.None;
            })
            .AddConsoleFormatter<KoanLogFormatter, ConsoleFormatterOptions>();
        });

        return services;
    }

    public static ILoggingBuilder AddKoanFormatter(this ILoggingBuilder builder)
    {
        return builder.AddConsole(options =>
        {
            options.FormatterName = "Koan";
                // When standard output carries a protocol, diagnostics belong on stderr.
                // The channel is process-derived, so this is already correct no matter when options
                // materialize.
                options.LogToStandardErrorThreshold =
                    Koan.Core.Hosting.KoanStandardStreams.IsStandardOutputProtocol
                        ? LogLevel.Trace
                        : LogLevel.None;
        })
        .AddConsoleFormatter<KoanLogFormatter, ConsoleFormatterOptions>();
    }
}