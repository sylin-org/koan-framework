using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace g1c1.GardenCoop.Hosting;

public static class ApplicationLifecycle
{
    public static void Configure(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var discovered = app.Urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var addresses = discovered.Length == 0
                ? "web defaults"
                : string.Join(", ", discovered);

            app.Logger.LogInformation(
                "Garden Cooperative slice is listening on {Addresses}. Close the window or press Ctrl+C to stop.",
                addresses);

            var preferred = discovered.FirstOrDefault(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            ?? discovered.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                app.Logger.LogInformation("Opening dashboard at {Url}", preferred);
                BrowserLauncher.OpenDashboard(app.Logger, preferred);
            }
        });

        app.Lifetime.ApplicationStopping.Register(() =>
            app.Logger.LogInformation("Shutting down Garden Cooperative slice – see you at dawn."));
    }
}
