using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.FieldExclusion.Tests;

/// <summary>
/// Integration coverage for <see cref="McpIgnoreAttribute"/> across the four surfaces it must reach:
/// the input schema, tool results (Tools mode), Code Mode results, and input deserialization.
/// Runs through real AddKoan() MCP discovery (ARCH-0079).
/// </summary>
public class McpFieldExclusionSpec : IClassFixture<FieldExclusionFixture>
{
    private readonly FieldExclusionFixture _fx;

    public McpFieldExclusionSpec(FieldExclusionFixture fx) => _fx = fx;

    [Fact(DisplayName = "Input schema omits input-excluded fields, keeps the rest")]
    public void Schema_OmitsInputExcludedFields()
    {
        var schema = _fx.GetToolInputSchema("catalog-item.upsert");
        var modelProps = (JObject?)schema["properties"]?["model"]?["properties"];
        modelProps.Should().NotBeNull("the upsert schema describes the entity model");

        modelProps!.ContainsKey("Name").Should().BeTrue();
        modelProps.ContainsKey("WriteOnlyToken").Should().BeTrue("output-only exclusion still allows input");

        modelProps.ContainsKey("InternalSecret").Should().BeFalse("[McpIgnore] hides it from input");
        modelProps.ContainsKey("ServerOwned").Should().BeFalse("[McpIgnore(Input)] hides it from input");
    }

    [Fact(DisplayName = "Tool results omit output-excluded fields, keep the rest")]
    public async Task Results_OmitOutputExcludedFields()
    {
        var id = await SeedViaRest("seed-public", "SEED-INTERNAL-SECRET", "SEED-SERVER-OWNED", "SEED-WRITEONLY");

        var result = await _fx.CallToolAsync("catalog-item.get-by-id", new JObject { ["id"] = id });
        var text = ContentText(result);
        text.Should().NotBeNullOrEmpty();

        text!.Should().Contain("seed-public", "Name is exposed");
        text.Should().Contain("SEED-SERVER-OWNED", "[McpIgnore(Input)] still returns the value");
        text.Should().NotContain("SEED-INTERNAL-SECRET", "[McpIgnore] hides it from results");
        text.Should().NotContain("SEED-WRITEONLY", "[McpIgnore(Output)] hides it from results");
    }

    [Fact(DisplayName = "Code Mode results do not leak output-excluded fields")]
    public async Task CodeMode_DoesNotLeakOutputExcludedField()
    {
        var id = await SeedViaRest("codemode-public", "CODEMODE-INTERNAL-SECRET", "codemode-server", "codemode-writeonly");

        var code = "function run() { const x = SDK.Entities.CatalogItem.getById('" + id + "'); SDK.Out.answer(JSON.stringify(x)); }";
        var result = await _fx.CallToolAsync("koan.code.execute", new JObject { ["code"] = code, ["correlationId"] = "field-exclusion-codemode" });
        var text = ContentText(result);
        text.Should().NotBeNullOrEmpty();

        text!.Should().NotContain("CODEMODE-INTERNAL-SECRET", "Code Mode shares the result serialization path");
        text.Should().Contain("codemode-public");
    }

    [Fact(DisplayName = "Upsert cannot set input-excluded fields (mass-assignment guard)")]
    public async Task Upsert_BlocksInputExcludedFields()
    {
        var model = new JObject
        {
            ["Name"] = "upsert-public",
            ["InternalSecret"] = "UPSERT-INTERNAL",
            ["ServerOwned"] = "UPSERT-SERVER",
            ["WriteOnlyToken"] = "UPSERT-WRITEONLY"
        };

        var upsert = await _fx.CallToolAsync("catalog-item.upsert", new JObject { ["model"] = model });
        var savedText = ContentText(upsert);
        savedText.Should().NotBeNullOrEmpty();
        var id = FindId(JToken.Parse(savedText!));
        id.Should().NotBeNullOrWhiteSpace();

        // Read the persisted entity via REST (REST does not apply [McpIgnore]).
        var http = _fx.CreateClient();
        var restBody = await http.GetStringAsync("/api/catalogitems/" + id);

        restBody.Should().Contain("upsert-public");
        restBody.Should().Contain("UPSERT-WRITEONLY", "[McpIgnore(Output)] does not block input");
        restBody.Should().NotContain("UPSERT-INTERNAL", "[McpIgnore] blocks the value from being set");
        restBody.Should().NotContain("UPSERT-SERVER", "[McpIgnore(Input)] blocks the value from being set");
    }

    [Fact(DisplayName = "Patch targeting an input-excluded field is rejected")]
    public async Task Patch_RejectsInputExcludedTarget()
    {
        var id = await SeedViaRest("patch-public", "patch-internal", "patch-server", "patch-writeonly");

        var patch = new JArray
        {
            new JObject { ["op"] = "replace", ["path"] = "/internalSecret", ["value"] = "tampered" }
        };
        var result = await _fx.CallToolAsync("catalog-item.patch", new JObject { ["id"] = id, ["patch"] = patch });

        result["isError"]?.Value<bool>().Should().BeTrue();
        ContentText(result).Should().Contain("cannot be modified", "the patch guard rejects input-excluded targets");
    }

    private async Task<string> SeedViaRest(string name, string internalSecret, string serverOwned, string writeOnlyToken)
    {
        var http = _fx.CreateClient();
        var body = new JObject
        {
            ["Name"] = name,
            ["InternalSecret"] = internalSecret,
            ["ServerOwned"] = serverOwned,
            ["WriteOnlyToken"] = writeOnlyToken
        }.ToString();

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await http.PostAsync("/api/catalogitems", content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        var id = FindId(JToken.Parse(responseBody));
        id.Should().NotBeNullOrWhiteSpace("REST seeding returns the created entity id");
        return id!;
    }

    private static string? ContentText(JToken callResult)
        => callResult["content"]?[0]?["text"]?.Value<string>();

    private static string? FindId(JToken entity)
        => entity["id"]?.Value<string>() ?? entity["Id"]?.Value<string>();
}
