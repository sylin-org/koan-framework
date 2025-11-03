using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Koan.Web.Hosting;

/// <summary>
/// Extension methods for Koan sample applications to reduce boilerplate hosting code.
/// Provides consistent logging, browser launching, and lifecycle management across samples.
/// </summary>
public static class SampleApplicationExtensions
{
    /// <summary>
    /// Configures simple console logging for sample applications.
    /// Clears default providers and adds single-line console output with timestamps.
    /// </summary>
    /// <param name="builder">The web application builder</param>
    /// <returns>The builder for method chaining</returns>
    public static WebApplicationBuilder ConfigureSampleLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        return builder;
    }

    /// <summary>
    /// Configures sample application lifecycle with startup/shutdown logging and optional browser launch.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <param name="sampleName">Display name for the sample (e.g., "S1 Web relationship demo")</param>
    /// <param name="startupMessage">Optional custom startup message. If null, uses default format.</param>
    /// <param name="shutdownMessage">Optional custom shutdown message. If null, uses default format.</param>
    /// <param name="launchBrowser">Whether to automatically launch browser on startup (default: true)</param>
    /// <returns>The app for method chaining</returns>
    public static WebApplication ConfigureSampleLifecycle(
        this WebApplication app,
        string sampleName,
        string? startupMessage = null,
        string? shutdownMessage = null,
        bool launchBrowser = true)
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

            var message = startupMessage ??
                          $"{sampleName} is listening on {{Addresses}}. Close the window or press Ctrl+C to stop.";

            app.Logger.LogInformation(message, addresses);

            if (launchBrowser)
            {
                var preferred = discovered.FirstOrDefault(static url =>
                                     url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                ?? discovered.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    app.Logger.LogInformation("Opening sample UI at {Url}", preferred);
                    LaunchBrowser(app.Logger, preferred);
                }
            }
        });

        app.Lifetime.ApplicationStopping.Register(() =>
        {
            var message = shutdownMessage ?? $"{sampleName} shutting down.";
            app.Logger.LogInformation(message);
        });

        return app;
    }

    /// <summary>
    /// Attempts to launch the default browser with the specified URL.
    /// Logs failure as debug message if unable to launch.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="url">URL to open in browser</param>
    public static void LaunchBrowser(ILogger logger, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to launch browser for {Url}", url);
        }
    }
}
