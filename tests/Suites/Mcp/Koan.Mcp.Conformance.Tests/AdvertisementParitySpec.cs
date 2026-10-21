using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Koan.Mcp.TestKit;
using Koan.Web.Authorization;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots the parity entities with the hardened default decision: an undeclared verb is forbidden.</summary>
public sealed class ForbidDefaultFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("parity-plain");

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton<IOptions<AuthorizeOptions>>(Microsoft.Extensions.Options.Options.Create(new AuthorizeOptions { DefaultDecision = AuthorizeDefault.Forbid }));
}

/// <summary>Boots the parity entities with the default posture, for the agent-grant cells.</summary>
public sealed class GrantParityFixture : McpHarnessFixtureBase
{
    protected override void ConfigureMcp(McpServerOptions options) => options.AllowedEntities.Add("parity-editor");
}

/// <summary>What <c>tools/list</c> and <c>koan://entities</c> show a principal, through the real dispatcher and handler.</summary>
internal static class Advertised
{
    public static async Task<string[]> Tools(McpHarnessFixtureBase fx, ClaimsPrincipal principal)
    {
        var dispatcher = fx.Services.GetRequiredService<McpRpcDispatcher>();
        var handler = fx.Services.GetRequiredService<McpServer>().CreateHandler();
        var response = await dispatcher.DispatchAsync(new JsonRpcEnvelope("2.0", "tools/list", new JObject(), 1), principal, handler, CancellationToken.None);
        return ((JArray)response!["result"]!["tools"]!).Select(t => t["name"]!.Value<string>()!).ToArray();
    }

    public static string[] CatalogVerbs(McpHarnessFixtureBase fx, string entity, ClaimsPrincipal principal)
    {
        var handler = fx.Services.GetRequiredService<McpServer>().CreateHandler();
        var read = handler.ReadResourceFor(EntityCatalogResourceProvider.ResourceUri, principal);
        var doc = JObject.Parse(read.Contents.Single().Text);
        var found = ((JArray)doc["entities"]!).OfType<JObject>().FirstOrDefault(e => e["name"]?.Value<string>() == entity);
        return found is null ? [] : ((JArray)found["verbs"]!).Select(v => v["operation"]!.Value<string>()!).ToArray();
    }

    public static ClaimsPrincipal Agent(string subject, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}

/// <summary>
/// SEC-0011: a listed verb is a callable verb. <c>tools/list</c> and <c>koan://entities</c> decide visibility with
/// the authorization ladder the call enforces, so the default decision hides what it denies.
/// </summary>
public sealed class ForbidDefaultParitySpec : IClassFixture<ForbidDefaultFixture>
{
    private readonly ForbidDefaultFixture _fx;

    public ForbidDefaultParitySpec(ForbidDefaultFixture fx) => _fx = fx;

    [Fact]
    public async Task A_forbid_default_hides_the_undeclared_verbs_it_denies()
    {
        var caller = Advertised.Agent("parity-forbidden");
        var collection = _fx.ResolveToolName("parity-plain", EntityEndpointOperationKind.Collection);

        var call = await _fx.CallToolAsAsync(collection, new JObject(), caller);
        McpHarnessFixtureBase.IsError(call).Should().BeTrue("the default decision forbids an undeclared verb: {0}", call);

        (await Advertised.Tools(_fx, caller)).Should().NotContain(collection, "tools/list must not advertise a verb the call denies");
        Advertised.CatalogVerbs(_fx, "parity-plain", caller).Should().BeEmpty("koan://entities must not advertise it either");
    }
}

/// <summary>SEC-0011: an agent grant that admits a call also reveals the verb in every listing.</summary>
public sealed class GrantParitySpec : IClassFixture<GrantParityFixture>
{
    private readonly GrantParityFixture _fx;

    public GrantParitySpec(GrantParityFixture fx) => _fx = fx;

    [Fact]
    public async Task An_agent_grant_reveals_the_verb_it_unlocks()
    {
        var caller = Advertised.Agent("parity-granted-agent");
        var collection = _fx.ResolveToolName("parity-editor", EntityEndpointOperationKind.Collection);

        (await Advertised.Tools(_fx, caller)).Should().NotContain(collection, "without the role or a grant the read is walled");

        var grant = new AgentGrant { Subject = "parity-granted-agent", Capability = "is:editor", Resource = nameof(ParityEditor) };
        await grant.Save();
        try
        {
            var call = await _fx.CallToolAsAsync(collection, new JObject(), caller);
            McpHarnessFixtureBase.IsError(call).Should().BeFalse("the grant admits the read: {0}", call);

            (await Advertised.Tools(_fx, caller)).Should().Contain(collection, "the verb the grant unlocks is listed");
            Advertised.CatalogVerbs(_fx, "parity-editor", caller).Should().Contain("Collection");
        }
        finally
        {
            await grant.Remove();
        }
    }
}
