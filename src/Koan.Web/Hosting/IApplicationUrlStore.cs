namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0081: where an application keeps the base URL it accepted at runtime. Koan holds the accepted value in memory
/// and never persists it; the application decides where it lives (a settings row, a file) and registers one store in
/// DI. Koan reads it once before the host serves and writes it through <see cref="KoanWebBaseUrl.Accept"/>, so an
/// accepted URL survives a restart.
/// </summary>
/// <remarks>
/// The store is a singleton: Koan resolves it once with the base URL. <see cref="Load"/> runs during module start,
/// inside the host's ambient scope, so an <c>Entity&lt;T&gt;</c> static reads the application's own data there.
/// </remarks>
public interface IApplicationUrlStore
{
    /// <summary>The URL the application accepted, as <see cref="Save"/> last received it, or null when none was.</summary>
    Task<string?> Load(CancellationToken ct);

    /// <summary>Keep <paramref name="applicationUrl"/>, already validated and normalised, as the accepted URL.</summary>
    Task Save(string applicationUrl, CancellationToken ct);

    /// <summary>
    /// The application's named posture under ARCH-0128: until it accepts a URL, and while nothing is configured, Koan
    /// derives absolute URLs from each request in Production too, so the application can be set up on the address its
    /// first request arrives at. It ends the moment a URL is accepted or configured; from then on every absolute URL is
    /// that URL, whatever <c>Host</c> says. Koan says so once in the log. False (the default) keeps WEB-0078's refusal in
    /// Production. It is this application's consent, not <c>Koan:AllowMagicInProduction</c>.
    /// </summary>
    bool DerivesUntilAccepted => false;
}
