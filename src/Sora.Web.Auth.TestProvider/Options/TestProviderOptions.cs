namespace Sora.Web.Auth.TestProvider.Options;

public sealed class TestProviderOptions
{
    public const string SectionPath = "Sora:Web:Auth:TestProvider";
    public bool Enabled { get; init; } = false;
    public string RouteBase { get; init; } = "/.testoauth";
    public string ClientId { get; init; } = "test-client";
    public string ClientSecret { get; init; } = "test-secret";
    public bool ExposeInDiscoveryOutsideDevelopment { get; init; } = false;
}
