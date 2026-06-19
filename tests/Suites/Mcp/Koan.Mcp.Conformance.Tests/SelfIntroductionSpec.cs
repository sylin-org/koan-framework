using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Mcp;
using Koan.Mcp.Options;
using Koan.Mcp.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// AN8 (docs/assessment/09 §11.1) — the <c>koan://self</c> self-introduction renders the projector's
/// per-grant output in two faces: a first-person <c>prose</c> greeting AND the <c>structured</c> contract
/// beneath it. The menu is authored by nobody — it reads from the app identity (<c>[KoanApp]</c> →
/// <see cref="KoanEnv"/>.CurrentSnapshot.Application) and the verbs visible to the caller. A capability the
/// caller can't reach is absent (a slice of charter T6 — admin/door tiers layer on with AN5).
/// </summary>
public sealed class SelfIntroductionSpec : IClassFixture<ConformanceFixture>
{
    private readonly ConformanceFixture _fx;

    public SelfIntroductionSpec(ConformanceFixture fx) => _fx = fx;

    [Fact]
    public async Task Self_resource_is_listed()
    {
        var resources = await _fx.ListResourcesAsync();
        resources.OfType<JObject>().Select(r => r["uri"]?.Value<string>())
            .Should().Contain(SelfResourceProvider.ResourceUri, "the framework ships koan://self");
    }

    [Fact]
    public async Task Self_has_both_prose_and_structured_faces()
    {
        var contents = await _fx.ReadResourceAsync(SelfResourceProvider.ResourceUri);
        contents.Should().NotBeNull();
        var doc = JObject.Parse(contents!["text"]!.Value<string>()!);

        // Prose face: a first-person greeting carrying the app identity.
        var prose = doc["prose"]?.Value<string>();
        prose.Should().NotBeNullOrEmpty();
        prose!.Should().StartWith("I'm ", "the greeting is first-person");
        prose.Should().Contain(KoanEnv.CurrentSnapshot.Application.Name, "the greeting names the application");

        // Structured face: the exact contract (identity + entities + verbs) — prose is never the only form.
        doc["identity"]!["name"]?.Value<string>().Should().Be(KoanEnv.CurrentSnapshot.Application.Name);
        var entities = (JArray)doc["entities"]!;
        entities.OfType<JObject>().Select(e => e["name"]?.Value<string>())
            .Should().Contain("gadget", "the visible entities appear in the structured contract");
    }

    [Fact]
    public void Self_omits_capabilities_the_caller_cannot_reach()
    {
        // Concrete anonymous remote principal: the scoped vault entity is absent from the self-projection
        // (walled-means-silent) — a tier you can't reach is invisible, not redacted.
        var provider = new SelfResourceProvider(
            _fx.Services.GetRequiredService<McpEntityRegistry>(),
            _fx.Services.GetRequiredService<Koan.Web.Authorization.IAccessGateCache>());

        var doc = JObject.Parse(provider.Read(SelfResourceProvider.ResourceUri, new ClaimsPrincipal(new ClaimsIdentity()))!.Text);
        var names = ((JArray)doc["entities"]!).OfType<JObject>().Select(e => e["name"]?.Value<string>()).ToList();

        names.Should().Contain("gadget", "a public entity is part of the anonymous self-projection");
        names.Should().NotContain("vault", "a scoped entity is absent from the self-projection for an unscoped caller");
    }
}
