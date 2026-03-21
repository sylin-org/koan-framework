using System.Net;

namespace S18.Prism.Services.SourcePulling;

internal static class UrlValidator
{
    public static bool IsSafeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != "http" && uri.Scheme != "https")
            return false;
        if (uri.IsLoopback)
            return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        // Block private IP ranges
        if (IPAddress.TryParse(uri.Host, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 10) return false;                           // 10.0.0.0/8
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)   // 172.16.0.0/12
                return false;
            if (bytes[0] == 192 && bytes[1] == 168) return false;       // 192.168.0.0/16
            if (bytes[0] == 169 && bytes[1] == 254) return false;       // 169.254.0.0/16 (metadata)
            if (bytes[0] == 127) return false;                          // 127.0.0.0/8
        }

        return true;
    }
}
