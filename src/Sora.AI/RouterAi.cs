using Sora.AI.Contracts;
using Sora.AI.Contracts.Routing;

namespace Sora.AI;

internal sealed class RouterAi : IAi
{
    private readonly IAiRouter _router;
    public RouterAi(IAiRouter router) => _router = router;
    public Task<Contracts.Models.AiChatResponse> PromptAsync(Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => _router.PromptAsync(request, ct);

    public IAsyncEnumerable<Contracts.Models.AiChatChunk> StreamAsync(Contracts.Models.AiChatRequest request, CancellationToken ct = default)
        => _router.StreamAsync(request, ct);

    public Task<Contracts.Models.AiEmbeddingsResponse> EmbedAsync(Contracts.Models.AiEmbeddingsRequest request, CancellationToken ct = default)
        => _router.EmbedAsync(request, ct);

    public Task<string> PromptAsync(string message, string? model = null, Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => _router.PromptAsync(new Contracts.Models.AiChatRequest { Messages = new() { new Contracts.Models.AiMessage("user", message) }, Model = model, Options = opts }, ct)
            .ContinueWith(t => t.Result.Text, ct);

    public IAsyncEnumerable<Contracts.Models.AiChatChunk> StreamAsync(string message, string? model = null, Contracts.Models.AiPromptOptions? opts = null, CancellationToken ct = default)
        => _router.StreamAsync(new Contracts.Models.AiChatRequest { Messages = new() { new Contracts.Models.AiMessage("user", message) }, Model = model, Options = opts }, ct);
}