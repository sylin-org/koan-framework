using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// Serializes the specs that each boot their own AddKoan host. Each host sets the static
/// <c>AppHost.Current</c>; running them in parallel would race on it (and the persisted-key spec reads
/// Entity&lt;T&gt; data through that ambient), so they share one collection to run sequentially.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AuthServerHostCollection
{
    public const string Name = "auth-server-host";
}
