using System.Net.Http.Json;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Samples.McpCodeMode.Tests;

public class CodeModeTodoParitySpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    private static readonly JsonSerializerSettings JsonOpts = new()
    {
        Formatting = Formatting.None
    };

    public CodeModeTodoParitySpec(TestPipelineFixture fx) => _fx = fx;

    private record RpcRequest(string Jsonrpc, string Method, object? @Params, string Id);
    private record RpcResponse<T>(string Jsonrpc, T? Result, object? Error, string Id);

    private async Task<JToken> CallToolAsync(string toolName, object arguments)
    {
        var jsonString = JsonConvert.SerializeObject(arguments, JsonOpts);
        var json = JObject.Parse(jsonString);
        var resultObj = await _fx.InvokeRpcAsync("tools/call", Guid.NewGuid().ToString("n"), toolName, json);
        var normalized = JsonConvert.SerializeObject(resultObj, JsonOpts);
        return JToken.Parse(normalized);
    }

    [Fact(DisplayName = "Code mode upsert + getById parity with REST entity endpoints")]
    public async Task CodeMode_ShouldMatchEntityRest()
    {
        // Arrange: create via code-mode script
    // Assumes MedTrials MCP service exposes a Todo entity; if not, this test will need updating to an available entity.
    // NOTE: Jint in current version does not support top-level async/await or async function semantics for our use-case.
    // Entity SDK methods are synchronous (they block on async internally), so we invoke them directly without await.
    var code = @"function run() { const t = SDK.Entities.Todo.upsert({ title: 'from-code', completed: false }); const fetched = SDK.Entities.Todo.getById(t.id); const title = fetched.title || fetched.Title; const completed = fetched.completed || fetched.Completed; SDK.Out.answer(JSON.stringify({ id: fetched.id || fetched.Id, title, completed })); }";

        var result = await CallToolAsync("koan.code.execute", new { code, entryFunction = (string?)null, correlationId = "test-parity-1" });
        result.Type.Should().Be(JTokenType.Object);
        var textProp = result["text"];
        string? text = textProp?.Type switch
        {
            JTokenType.String => textProp!.Value<string>(),
            JTokenType.Object => textProp!.ToString(Formatting.None),
            _ => null
        };
    text.Should().NotBeNullOrEmpty();
    var answer = JToken.Parse(text!);
    var id = answer["id"]?.Value<string>();
        id.Should().NotBeNullOrWhiteSpace();
    answer["title"]?.Value<string>().Should().Be("from-code");

        // Act: fetch same entity via REST controller (EntityController<Todo>)
        var http = _fx.CreateClient();
    var restEntityStr = await http.GetStringAsync($"/api/todos/{id}");
    var restEntity = JToken.Parse(restEntityStr);

        // Assert parity
    restEntity["id"]?.Value<string>().Should().Be(id);
    restEntity["title"]?.Value<string>().Should().Be("from-code");
    }

    [Fact(DisplayName = "Code mode collection listing should return inserted entity")] 
    public async Task CodeMode_ListShouldContainEntity()
    {
        // Insert via code
    var code = @"function run() { SDK.Entities.Todo.upsert({ title: 'list-check', completed: true }); SDK.Out.answer(JSON.stringify({ ok: true })); }";
    await CallToolAsync("koan.code.execute", new { code, correlationId = "test-list-1" });

        // Query via code for collection
    // collection() returns a paging object with an 'items' array; assert inserted entity present.
    var listCode = @"function run() { const col = SDK.Entities.Todo.collection(); SDK.Out.answer(JSON.stringify(col)); }";
    var listResult = await CallToolAsync("koan.code.execute", new { code = listCode, correlationId = "test-list-2" });
    var listTextProp = listResult["text"];
    string? listText = listTextProp?.Type switch
    {
        JTokenType.String => listTextProp!.Value<string>(),
        JTokenType.Object => listTextProp!.ToString(Formatting.None),
        _ => null
    };
    listText.Should().NotBeNullOrEmpty();
    var collection = JToken.Parse(listText!);
    var items = collection["items"] as JArray ?? new JArray();
    items.Should().NotBeNull();
    items.Count.Should().BeGreaterThan(0);
    items.Any(t => string.Equals(t["Title"]?.Value<string>(), "list-check", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(t["title"]?.Value<string>(), "list-check", StringComparison.OrdinalIgnoreCase))
         .Should().BeTrue("Expected collection to contain inserted 'list-check' entity");
    }

    [Fact(DisplayName = "Code mode getById missing entity does not throw")]
    public async Task CodeMode_GetById_Missing()
    {
        var code = @"function run() { try { const fetched = SDK.Entities.Todo.getById('does-not-exist-12345'); if (!fetched) { SDK.Out.answer(JSON.stringify({ mode: 'null' })); } else { SDK.Out.answer(JSON.stringify({ mode: 'object', hasId: !!(fetched.id || fetched.Id) })); } } catch (e) { SDK.Out.answer(JSON.stringify({ mode: 'error', error: '' + e })); } }";
        var result = await CallToolAsync("koan.code.execute", new { code, correlationId = "test-missing-1" });
        var textProp = result["text"];        
        string? text = textProp?.Type switch
        {
            JTokenType.String => textProp!.Value<string>(),
            JTokenType.Object => textProp!.ToString(Formatting.None),
            _ => null
        };
        text.Should().NotBeNullOrEmpty();
        var answer = JToken.Parse(text!);
        var mode = answer["mode"]?.Value<string>();
        mode.Should().NotBeNull();
        // Accept either null (preferred) or object without id (lenient); error is failure
        mode!.Should().NotBe("error", because: "missing getById should not throw");
        if (mode == "object")
        {
            // If object returned, do not require hasId semantics; just ensure flag present
            answer["hasId"].Should().NotBeNull();
        }
    }

    [Fact(DisplayName = "Code mode invalid tool invocation surfaces diagnostic")]
    public async Task CodeMode_InvalidTool_Diagnostic()
    {
        // Attempt to call a non-existent tool name
        var bogusArgs = new { code = "function run(){}", correlationId = "invalid-tool-1" };
        var jsonString = JsonConvert.SerializeObject(bogusArgs, JsonOpts);
        var json = JObject.Parse(jsonString);
        var resultObj = await _fx.InvokeRpcAsync("tools/call", Guid.NewGuid().ToString("n"), "koan.code.execute.nope", json);
        var normalized = JsonConvert.SerializeObject(resultObj, JsonOpts);
        var token = JToken.Parse(normalized);
        // Expect an error surface; depending on RPC layer this may appear under 'error' or absence of 'text'.
        (token["error"] != null || token["text"] == null).Should().BeTrue("Expected error or absence of text for invalid tool invocation.");
    }
}
