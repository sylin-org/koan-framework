using System.Net;
using System.Text;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length != 1)
{
    throw new ArgumentException("Expected one workspace path argument.");
}

var workspace = Path.GetFullPath(args[0]);
Directory.CreateDirectory(workspace);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = "Test"
});
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Koan:Environment"] = "Test",
    ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
    ["Koan:Data:Sources:Default:Adapter"] = "json",
    ["Koan:Data:Sources:Default:json:DirectoryPath"] = workspace,
    ["Koan:Data:Sources:Default:json:Layout"] = "IndividualFiles",
    ["Koan:Data:Sources:Default:json:IndividualFilePath"] = "{id}/article.json"
});
builder.Services.AddKoan();

await using var app = builder.Build();
await app.StartAsync();
using var hostScope = AppHost.PushScope(app.Services);

try
{
    var server = app.Services.GetRequiredService<IServer>();
    var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
        ?? throw new InvalidOperationException("Kestrel did not publish a listening address.");
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    const string id = "reviewed-article";
    var articlePath = Path.Combine(workspace, id, "article.json");
    var mediaPath = Path.Combine(workspace, id, "media", "keep.txt");

    using (var created = await client.PostAsync(
        "/api/articles",
        new StringContent("""{"id":"reviewed-article","title":"Created through HTTP"}""", Encoding.UTF8, "application/json")))
    {
        created.EnsureSuccessStatusCode();
    }
    Require(File.Exists(articlePath), "HTTP create did not write {id}/article.json.");

    using (var fetched = await client.GetAsync($"/api/articles/{id}"))
    {
        fetched.EnsureSuccessStatusCode();
        var body = JObject.Parse(await fetched.Content.ReadAsStringAsync());
        Require(Value(body, "title") == "Created through HTTP", "HTTP read returned the wrong article.");
    }

    var throughStatics = await Article.Get(id)
        ?? throw new InvalidOperationException("Entity<T>.Get did not route to the JSON repository.");
    throughStatics.Title = "Saved through Entity statics";
    await throughStatics.Save();

    var external = JObject.Parse(await File.ReadAllTextAsync(articlePath));
    external["corpus"] = "imported";
    external["arbitrary"] = new JObject { ["rank"] = 7 };
    await File.WriteAllTextAsync(articlePath, external.ToString(Formatting.None));

    var externallyEdited = await Article.Get(id)
        ?? throw new InvalidOperationException("Entity<T>.Get did not observe the external file edit.");
    Require(externallyEdited.Metadata["corpus"]?.Value<string>() == "imported",
        "JsonExtensionData did not materialize an unknown scalar field.");
    Require(externallyEdited.Metadata["arbitrary"]?["rank"]?.Value<int>() == 7,
        "JsonExtensionData did not materialize an unknown object field.");
    externallyEdited.Title = "Unknown fields preserved";
    await externallyEdited.Save();

    var persisted = JObject.Parse(await File.ReadAllTextAsync(articlePath));
    Require(Value(persisted, "corpus") == "imported", "Entity save discarded an unknown scalar field.");
    Require(persisted.GetValue("arbitrary", StringComparison.OrdinalIgnoreCase)?["rank"]?.Value<int>() == 7,
        "Entity save discarded an unknown object field.");
    Require(persisted.GetValue(nameof(Article.Metadata), StringComparison.OrdinalIgnoreCase) is null,
        "JsonExtensionData was persisted as a wrapper property.");

    persisted["title"] = "Updated through HTTP";
    using (var updated = await client.PostAsync(
        "/api/articles",
        new StringContent(persisted.ToString(Formatting.None), Encoding.UTF8, "application/json")))
    {
        updated.EnsureSuccessStatusCode();
    }
    using (var fetched = await client.GetAsync($"/api/articles/{id}"))
    {
        fetched.EnsureSuccessStatusCode();
        var body = JObject.Parse(await fetched.Content.ReadAsStringAsync());
        Require(Value(body, "title") == "Updated through HTTP", "HTTP update was not persisted.");
        Require(Value(body, "corpus") == "imported", "HTTP update discarded extension data.");
    }

    Directory.CreateDirectory(Path.GetDirectoryName(mediaPath)!);
    await File.WriteAllTextAsync(mediaPath, "application-owned media");
    using (var deleted = await client.DeleteAsync($"/api/articles/{id}"))
    {
        deleted.EnsureSuccessStatusCode();
    }
    Require(!File.Exists(articlePath), "HTTP delete did not remove article.json.");
    Require(File.Exists(mediaPath), "HTTP delete removed application-owned sibling media.");
    Require(await Article.Get(id) is null, "Entity statics still returned the deleted article.");

    Console.WriteLine("PACKAGE-CONSUMER|APP-JSON|PASS");
}
finally
{
    await app.StopAsync();
}

static string? Value(JObject value, string property) =>
    value.GetValue(property, StringComparison.OrdinalIgnoreCase)?.Value<string>();

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";

    [JsonExtensionData]
    public IDictionary<string, JToken> Metadata { get; set; } =
        new Dictionary<string, JToken>(StringComparer.Ordinal);
}

[Route("api/articles")]
public sealed class ArticlesController : EntityController<Article>;
