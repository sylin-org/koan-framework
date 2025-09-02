using System.Threading;
using System.Threading.Tasks;
using Sora.Messaging;

namespace Sora.Flow.Actions;

public sealed class ControlResponse<T>
{
    public required string Model { get; init; }
    public required string Verb { get; init; }
    public required string Status { get; init; }
    public string? Message { get; init; }
    public string? CorrelationId { get; init; }
    public required T Payload { get; init; }

    public Task Send(CancellationToken ct = default) => MessagingExtensions.Send(this, ct);
}
