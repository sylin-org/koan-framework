namespace Koan.Web.Hosting;

/// <summary>The feature every request <see cref="KoanWebPipeline"/> makes carries: it runs inside the request in hand's limiter permit and
/// arrived on no origin (ARCH-0144).</summary>
internal sealed class InProcessMark
{
    public static readonly InProcessMark Instance = new();

    private InProcessMark() { }
}
