using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace g1c1.GardenCoop.Hosting;

public static class BrowserLauncher
{
    public static void OpenDashboard(ILogger logger, string url)
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
