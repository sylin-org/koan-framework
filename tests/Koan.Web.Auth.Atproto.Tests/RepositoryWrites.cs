using System.Net;
using System.Text.Json.Nodes;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Xunit;

namespace Koan.Web.Auth.Atproto.Tests;

/// <summary>A person who granted a write has their own repository written as them: a record put, put again in place,
/// deleted, and a blob uploaded; without the grant, nothing is held and a write is refused as needing it.</summary>
public sealed class RepositoryWrites
{
    private const string Documents = "repo:site.standard.document";
    private const string Blobs = "blob:image/*";

    [Fact]
    public async Task A_granted_session_puts_replaces_deletes_and_uploads_in_its_own_repository()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var node = world.Node(scopes: ["atproto", Documents, Blobs]);
        var did = world.Dids[0];
        await world.SignIn(node, did, "atproto", Documents, Blobs);
        IAtprotoRepository repository = new AtprotoRepository(node.Sessions);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await repository.Holds(did, [Documents, Blobs], ct));
        Assert.False(await repository.Holds(did, ["repo:app.bsky.feed.post"], ct));

        var blob = await repository.Upload(did, new byte[] { 1, 2, 3 }, "image/png", ct);
        Assert.Equal("blob", blob["$type"]!.GetValue<string>());
        Assert.Equal(3, blob["size"]!.GetValue<int>());

        var first = await repository.Put(did, "site.standard.document", "3mwgqaw5is227",
            new JsonObject { ["$type"] = "site.standard.document", ["title"] = "Mending with gold", ["coverImage"] = blob.DeepClone() }, ct);
        var address = $"at://{did}/site.standard.document/3mwgqaw5is227";
        Assert.Equal(address, first.Uri);
        var second = await repository.Put(did, "site.standard.document", "3mwgqaw5is227",
            new JsonObject { ["$type"] = "site.standard.document", ["title"] = "Mending with lacquer" }, ct);
        Assert.Equal(address, second.Uri);
        Assert.NotEqual(first.Cid, second.Cid);
        Assert.Contains("Mending with lacquer", world.Pds.Records[address]);

        await repository.Delete(did, "site.standard.document", "3mwgqaw5is227", ct);
        Assert.False(world.Pds.Records.ContainsKey(address));
        await repository.Delete(did, "site.standard.document", "3mwgqaw5is227", ct);
    }

    [Fact]
    public async Task A_session_whose_grant_ended_holds_nothing_and_its_write_needs_the_grant_again()
    {
        await using var world = await SessionWorld.Start(accounts: 1);
        var node = world.Node(scopes: ["atproto", Documents]);
        var did = world.Dids[0];
        await world.SignIn(node, did, "atproto", Documents);
        IAtprotoRepository repository = new AtprotoRepository(node.Sessions);
        var ct = TestContext.Current.CancellationToken;
        Assert.False(await repository.Holds(SessionWorld.Did(2), [Documents], ct));

        world.Pds.End(did);
        var refused = await Assert.ThrowsAsync<AtprotoRepositoryException>(() =>
            repository.Put(did, "site.standard.document", "3mwgqaw5is227", new JsonObject { ["title"] = "Mending with gold" }, ct));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.True(refused.NeedsGrant);
        Assert.False(refused.Transient);
    }
}
