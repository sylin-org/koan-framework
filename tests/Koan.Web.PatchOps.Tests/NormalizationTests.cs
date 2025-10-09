using FluentAssertions;
using Koan.Data.Abstractions.Instructions;
using Microsoft.AspNetCore.JsonPatch;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Koan.Web.PatchOps.Tests;

public class NormalizationTests
{
    private sealed class DummyEntity : Koan.Data.Abstractions.IEntity<string>
    {
        public string Id { get; set; } = "a";
        public string? Name { get; set; }
        public int Count { get; set; }
        public string? Bio { get; set; }
    }

    // Local normalizer replicating controller semantics without reflection
    private static PatchPayload<string> NormalizeFromJsonPatch(string id, JsonPatchDocument<DummyEntity> doc)
    {
        var ops = doc.Operations.Select(o => new PatchOp(
            o.op,
            o.path,
            o.from,
            o.value is null ? null : JToken.FromObject(o.value)
        )).ToList();
        return new PatchPayload<string>(id, null, null, "json-patch", ops, new PatchOptions());
    }

    private static PatchPayload<string> NormalizeFromMergePatch(string id, JToken body)
        => NormalizeObjectToOps(id, body, kindHint: "merge-patch", mergeSemantics: true);

    private static PatchPayload<string> NormalizeFromPartialJson(string id, JToken body)
        => NormalizeObjectToOps(id, body, kindHint: "partial-json", mergeSemantics: false);

    private static PatchPayload<string> NormalizeObjectToOps(string id, JToken body, string kindHint, bool mergeSemantics)
    {
        var ops = new List<PatchOp>();
        void Walk(JToken token, string basePath)
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties())
                {
                    var path = basePath + "/" + p.Name;
                    if (p.Value.Type == JTokenType.Object)
                    {
                        Walk(p.Value, path);
                    }
                    else if (p.Value.Type == JTokenType.Null)
                    {
                        if (mergeSemantics)
                            ops.Add(new PatchOp("remove", path, null, null));
                        else
                            ops.Add(new PatchOp("replace", path, null, JValue.CreateNull()));
                    }
                    else
                    {
                        ops.Add(new PatchOp("replace", path, null, p.Value.DeepClone()));
                    }
                }
            }
            else
            {
                // primitives/arrays at root -> replace entire document path
                ops.Add(new PatchOp("replace", basePath, null, token.DeepClone()));
            }
        }

        Walk(body, "");
        // Ensure pointers start at root ('/prop')
        ops = ops.Select(o => o with { Path = o.Path.StartsWith('/') ? o.Path : "/" + o.Path.TrimStart('/') }).ToList();
        return new PatchPayload<string>(id, null, null, kindHint, ops, new PatchOptions());
    }

    [Fact]
    public void JsonPatch_Normalizes_OneToOne()
    {
        var resolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver();
        var doc = new JsonPatchDocument<DummyEntity>(new List<Microsoft.AspNetCore.JsonPatch.Operations.Operation<DummyEntity>>(), resolver);
        doc.Replace(e => e.Name!, "Leo");
        doc.Remove(e => e.Bio!);
        var payload = NormalizeFromJsonPatch("x", doc);
        payload.Ops.Should().HaveCount(2);
        payload.Ops[0].Op.Should().Be("replace");
        payload.Ops[0].Path.Should().Be("/name");
        payload.Ops[1].Op.Should().Be("remove");
        payload.Ops[1].Path.Should().Be("/bio");
    }

    [Fact]
    public void MergePatch_Normalizes_RemoveAndReplace()
    {
        var body = JObject.Parse("{ \"name\": \"Leo\", \"bio\": null }");
        var payload = NormalizeFromMergePatch("x", body);
        payload.Ops.Should().HaveCount(2);
        payload.Ops[0].Op.Should().Be("replace");
        payload.Ops[0].Path.Should().Be("/name");
        payload.Ops[0].Value.Should().NotBeNull();
        payload.Ops[0].Value!.Type.Should().Be(JTokenType.String);
        payload.Ops[0].Value!.Value<string>().Should().Be("Leo");
        payload.Ops[1].Op.Should().Be("remove");
        payload.Ops[1].Path.Should().Be("/bio");
        payload.Ops[1].Value.Should().BeNull();
    }

    [Fact]
    public void PartialJson_Normalizes_ReplaceAndNullReplace()
    {
        var body = JObject.Parse("{ \"name\": \"Leo\", \"bio\": null }");
        var payload = NormalizeFromPartialJson("x", body);
        payload.Ops.Should().HaveCount(2);
        payload.Ops[0].Op.Should().Be("replace");
        payload.Ops[0].Path.Should().Be("/name");
        payload.Ops[1].Op.Should().Be("replace");
        payload.Ops[1].Path.Should().Be("/bio");
    }
}
