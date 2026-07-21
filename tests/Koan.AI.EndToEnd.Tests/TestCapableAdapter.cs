using System.Runtime.CompilerServices;
using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Models;
using Koan.AI.Contracts;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// A test adapter with configurable capabilities for end-to-end testing.
/// Provides fake responses through the full adapter pipeline.
/// </summary>
internal sealed class TestCapableAdapter : IAiAdapter, IChatAdapter, IEmbedAdapter
{
    private readonly HashSet<string> _capabilities;

    public TestCapableAdapter(string id, params string[] capabilities)
    {
        Id = id;
        Name = id;
        Type = id;
        _capabilities = new HashSet<string>(capabilities, StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }
    public string Name { get; }
    public string Type { get; }
    public IReadOnlySet<string> Capabilities => _capabilities;
    public bool HasCapability(string capability) => _capabilities.Contains(capability);

    public IAiModelManager? ModelManager =>
        HasCapability(AiCapability.Pull) ? new TestModelManager(Id) : null;

    public bool CanServe(AiChatRequest request) => HasCapability(AiCapability.Chat);

    public Task<AiChatResponse> Chat(AiChatRequest request, CancellationToken ct = default)
        => Task.FromResult(new AiChatResponse
        {
            Text = $"Response from {Id}",
            Model = "test-model",
            AdapterId = Id
        });

    public async IAsyncEnumerable<AiChatChunk> Stream(
        AiChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield return new AiChatChunk { DeltaText = $"Streamed from {Id}", Model = "test-model" };
    }

    public Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest request, CancellationToken ct = default)
        => Task.FromResult(new AiEmbeddingsResponse
        {
            Vectors = request.Input.Select(_ => new float[] { 0.1f, 0.2f, 0.3f }).ToList(),
            Model = "test-embed"
        });

    public Task<IReadOnlyList<AiModelDescriptor>> ListModels(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModelDescriptor>>(
            [new AiModelDescriptor { Name = "test-model", AdapterId = Id }]);
}

internal sealed class TestModelManager : IAiModelManager
{
    private readonly string _adapterId;

    public TestModelManager(string adapterId) => _adapterId = adapterId;

    public Task<AiModelOperationResult> EnsureInstalled(
        AiModelOperationRequest request, CancellationToken ct)
        => Task.FromResult(new AiModelOperationResult
        {
            Success = true,
            OperationPerformed = true,
            Message = $"Model {request.Model} installed on {_adapterId}"
        });

    public Task<AiModelOperationResult> Refresh(
        AiModelOperationRequest request, CancellationToken ct)
        => Task.FromResult(new AiModelOperationResult { Success = true });

    public Task<AiModelOperationResult> Flush(
        AiModelOperationRequest request, CancellationToken ct)
        => Task.FromResult(new AiModelOperationResult { Success = true });

    public Task<IReadOnlyList<AiModelDescriptor>> ListManagedModels(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([]);
}
