using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CarpaNet.OAuth;

namespace Koan.Web.Auth.Connector.Atproto.Protocol;

/// <summary>A record as the account's PDS stored it: its <c>at://</c> address and the content id of this version.</summary>
public sealed record AtprotoRecord(string Uri, string Cid);

/// <summary>
/// The account's own repository, written as the account through its signed-in session: a record put at its key,
/// whether it is new or replaces the one there; a record deleted; a blob uploaded for a record to name. Every call goes
/// to the DID's verified PDS through <see cref="AtprotoSessions.Send"/>, with its DPoP, refresh and network guards. The
/// session must hold the scope the write needs, which the application asks for with <see cref="AtprotoChallenge"/>
/// when the person first chooses the write; <see cref="Holds"/> says whether it does.
/// </summary>
public interface IAtprotoRepository
{
    /// <summary>Whether <paramref name="did"/>'s session holds every one of <paramref name="scopes"/>: false where it has
    /// no session this application can use.</summary>
    Task<bool> Holds(string did, IReadOnlyCollection<string> scopes, CancellationToken ct = default);

    /// <summary>Puts <paramref name="record"/> at <paramref name="collection"/>/<paramref name="rkey"/> in
    /// <paramref name="did"/>'s repository, creating it or replacing it, so the same put sent again lands once.</summary>
    Task<AtprotoRecord> Put(string did, string collection, string rkey, JsonObject record, CancellationToken ct = default);

    /// <summary>Deletes the record at <paramref name="collection"/>/<paramref name="rkey"/>; one already gone is deleted.</summary>
    Task Delete(string did, string collection, string rkey, CancellationToken ct = default);

    /// <summary>Uploads <paramref name="content"/> as a blob of <paramref name="mimeType"/> and answers the blob reference
    /// a record names it by.</summary>
    Task<JsonObject> Upload(string did, ReadOnlyMemory<byte> content, string mimeType, CancellationToken ct = default);
}

/// <summary>
/// The PDS answered a repository call with a refusal: its HTTP status and the XRPC error name it gave, never its text.
/// <see cref="Transient"/> says whether the same call can pass later — the PDS was busy, limited the rate or failed —
/// and <see cref="NeedsGrant"/> whether the session no longer holds what the call needs, so the person grants it again.
/// </summary>
public sealed class AtprotoRepositoryException(HttpStatusCode status, string? error, string nsid)
    : Exception($"The PDS refused {nsid} with {(int)status}{(error is null ? "" : " " + error)}.")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>The XRPC error name, such as <c>InvalidRecord</c>, where the PDS gave one.</summary>
    public string? Error { get; } = error;

    public bool Transient => (int)Status >= 500 || Status == HttpStatusCode.TooManyRequests;

    public bool NeedsGrant => Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        || Error is "InsufficientScope" or "InvalidToken" or "ExpiredToken";
}

internal sealed class AtprotoRepository(AtprotoSessions sessions) : IAtprotoRepository
{
    /// <summary>The largest answer a repository call reads: a record's address and content id, or a blob reference.</summary>
    private const long AnswerBytes = 64 * 1024;

    public async Task<bool> Holds(string did, IReadOnlyCollection<string> scopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        AtprotoSessions.SessionMetadata? session;
        try
        {
            session = await sessions.GetSessionMetadata(did, ct);
        }
        catch (Exception unusable) when (unusable is InvalidOperationException or OAuthException)
        {
            // A session that no longer matches its identity or cannot be restored holds nothing until granted again.
            return false;
        }

        return session is not null && scopes.All(scope => session.Scopes.Contains(scope, StringComparer.Ordinal));
    }

    public async Task<AtprotoRecord> Put(string did, string collection, string rkey, JsonObject record, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);
        ArgumentNullException.ThrowIfNull(record);
        var body = new JsonObject { ["repo"] = did, ["collection"] = collection, ["rkey"] = rkey, ["record"] = record.DeepClone() };
        using var answer = await Call(did, "com.atproto.repo.putRecord", Json(body), ct);
        var root = answer.RootElement;
        return root.TryGetProperty("uri", out var uri) && uri.GetString() is { Length: > 0 } at
               && root.TryGetProperty("cid", out var cid) && cid.GetString() is { Length: > 0 } content
            ? new AtprotoRecord(at, content)
            : throw new InvalidOperationException("The PDS answered com.atproto.repo.putRecord with no record address.");
    }

    public async Task Delete(string did, string collection, string rkey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(rkey);
        var body = new JsonObject { ["repo"] = did, ["collection"] = collection, ["rkey"] = rkey };
        using var _ = await Call(did, "com.atproto.repo.deleteRecord", Json(body), ct);
    }

    public async Task<JsonObject> Upload(string did, ReadOnlyMemory<byte> content, string mimeType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        var bytes = new ReadOnlyMemoryContent(content);
        bytes.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        using var answer = await Call(did, "com.atproto.repo.uploadBlob", bytes, ct);
        return answer.RootElement.TryGetProperty("blob", out var blob) && blob.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(blob.GetRawText())!.AsObject()
            : throw new InvalidOperationException("The PDS answered com.atproto.repo.uploadBlob with no blob.");
    }

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private async Task<JsonDocument> Call(string did, string nsid, HttpContent content, CancellationToken ct)
    {
        using (content)
        using (var response = await sessions.Send(did, nsid, HttpMethod.Post, AnswerBytes, content: content, ct: ct))
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            JsonDocument? answer = null;
            try
            {
                answer = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            catch (JsonException)
            {
                // An answer with no JSON body: a deletion's may be empty, and a refusal still says its status.
            }

            if (response.IsSuccessStatusCode) return answer ?? JsonDocument.Parse("{}");
            using (answer)
            {
                var error = answer?.RootElement.ValueKind == JsonValueKind.Object && answer.RootElement.TryGetProperty("error", out var name)
                    ? name.GetString() : null;
                throw new AtprotoRepositoryException(response.StatusCode, error, nsid);
            }
        }
    }
}
