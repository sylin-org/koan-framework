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
                // When a capability owns stdout for a protocol, diagnostics belong on stderr.
                // Evaluated when options materialize (after Build), so a claim made during
                // composition is honored even though this callback was registered earlier.
                options.LogToStandardErrorThreshold =
                    Koan.Core.Hosting.KoanStandardStreams.IsStandardOutputClaimed
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
                // When a capability owns stdout for a protocol, diagnostics belong on stderr.
                // Evaluated when options materialize (after Build), so a claim made during
                // composition is honored even though this callback was registered earlier.
                options.LogToStandardErrorThreshold =
                    Koan.Core.Hosting.KoanStandardStreams.IsStandardOutputClaimed
                        ? LogLevel.Trace
                        : LogLevel.None;
        })
        .AddConsoleFormatter<KoanLogFormatter, ConsoleFormatterOptions>();
    }
}