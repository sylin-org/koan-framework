using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace S1.Web.Hosting;

public static class ApplicationLifecycle
{
    public static void Configure(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var discovered = app.Urls
                .Where(static url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var addresses = discovered.Length == 0
                ? "framework defaults"
                : string.Join(", ", discovered);

            app.Logger.LogInformation(
                "S1 Web relationship demo running on {Addresses}. Close the window or press Ctrl+C to stop.",
                addresses);

            var preferred = discovered.FirstOrDefault(static url =>
                                 url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            ?? discovered.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                app.Logger.LogInformation("Opening sample UI at {Url}", preferred);
                BrowserLauncher.Open(app.Logger, preferred);
            }
        });

        app.Lifetime.ApplicationStopping.Register(() =>
            app.Logger.LogInformation("S1 Web relationship demo shutting down."));
    }
}
