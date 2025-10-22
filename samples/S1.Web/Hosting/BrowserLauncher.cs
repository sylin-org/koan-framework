using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace S1.Web.Hosting;

public static class BrowserLauncher
{
    public static void Open(ILogger logger, string url)
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
