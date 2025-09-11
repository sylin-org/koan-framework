using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Sora.Core.Logging;

public static class LoggingServiceCollectionExtensions
{
    public static IServiceCollection AddSoraLogging(this IServiceCollection services)
    {
        // Add the context registry system first
        services.AddSoraLoggingContexts();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole(options =>
            {
                options.FormatterName = "sora";
            })
            .AddConsoleFormatter<SoraLogFormatter, ConsoleFormatterOptions>();
        });

        return services;
    }

    public static ILoggingBuilder AddSoraFormatter(this ILoggingBuilder builder)
    {
        return builder.AddConsole(options =>
        {
            options.FormatterName = "sora";
        })
        .AddConsoleFormatter<SoraLogFormatter, ConsoleFormatterOptions>();
    }
}