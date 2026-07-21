using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.Registry;
using Koan.Testing.Integration;
using Koan.Web.Auth.Contributors.Builtin;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Flow.Builtin;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// Canon for the auth de-split (ARCH-0086 Facet 2 stage b): <c>IKoanAuthFlowHandler</c> implementations
/// are discovered through the single <c>KoanRegistry</c> authority via the
/// <c>[KoanDiscoverable]</c> marker — NOT the old bespoke <c>AppDomain.CurrentDomain.GetAssemblies()</c>
/// scan (which missed lazily-loaded Koan assemblies). Proven end-to-end through real <c>AddKoan()</c>
/// reflective discovery per ARCH-0079.
/// </summary>
public sealed class AuthDiscoverableContributorSpec
{
    [Fact]
    public async Task Bootstrap_routes_auth_flow_handler_discovery_through_the_registry()
    {
        await using var host = await PillarHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        // The registry — populated by the source generator (Koan.Web.Auth, build-time) and the runtime
        // RegistryManifestLoader fallback — is the discovery authority, and it surfaces the built-in
        // [KoanDiscoverable] handler without any AppDomain scan.
        KoanRegistry.GetDiscoveredImplementors(typeof(IKoanAuthFlowHandler))
            .Should().Contain(typeof(RoleListFileContributor));

        // ...and AddKoanWebAuth wired that registry result into DI as a resolvable scoped handler.
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetServices<IKoanAuthFlowHandler>()
            .Select(c => c.GetType())
            .Should().Contain(typeof(RoleListFileContributor));
    }

    [Fact]
    public async Task Discovered_flow_handlers_register_builtins_including_the_migrated_contributor()
    {
        await using var host = await PillarHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var handlerTypeNames = scope.ServiceProvider.GetServices<IKoanAuthFlowHandler>()
            .Select(h => h.GetType().Name)
            .ToArray();

        // The built-in JSON challenge handler is discovered + registered...
        handlerTypeNames.Should().Contain(nameof(JsonChallengeHandler));
        // ...and the migrated built-in contributor is now discovered as a flow handler directly
        // (no LegacyAuthContributorAdapter shim — the legacy contract was retired in E5).
        handlerTypeNames.Should().Contain(nameof(RoleListFileContributor));
    }
}
