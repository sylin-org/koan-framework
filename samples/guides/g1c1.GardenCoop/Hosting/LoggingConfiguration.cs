using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace g1c1.GardenCoop.Hosting;

public static class LoggingConfiguration
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
    }
}
