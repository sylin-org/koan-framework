using System.Text.Json;
using Koan.Communication.Adapters;

namespace Koan.Communication.Runtime;

internal static class CommunicationContractIdentity
{
    private const int Version = 1;

    public static string Transport(Type entityType)
        => $"entity:{RequiredName(entityType)}@{Version}";

    public static string Events(Type entityType, Type eventType)
        => $"event:{RequiredName(entityType)}:{RequiredName(eventType)}@{Version}";

    public static string FrameworkSignal<TSignal>()
        where TSignal : struct, Signals.IFrameworkSignal<TSignal>
        => TSignal.ContractId;

    private static string RequiredName(Type type)
        => type.FullName ?? throw new InvalidOperationException(
            $"Communication contract type '{type}' has no stable full name.");
}

internal sealed record CommunicationWireEnvelope(
    int Schema,
    string Mesh,
    CommunicationLane Lane,
    string Channel,
    string Contract,
    Guid OperationId,
    long Ordinal,
    string Payload,
    IReadOnlyDictionary<string, string>? Context,
    Guid? OccurrenceId = null,
    DateTimeOffset? OccurredAt = null,
    bool HasDetails = false,
    string? DetailsPayload = null);

internal static class CommunicationWireCodec
{
    internal const int SchemaVersion = 2;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ReadOnlyMemory<byte> Encode(CommunicationWireEnvelope envelope)
        => JsonSerializer.SerializeToUtf8Bytes(envelope, Options);

    public static CommunicationWireEnvelope Decode(ReadOnlyMemory<byte> payload)
    {
        try
        {
            return JsonSerializer.Deserialize<CommunicationWireEnvelope>(payload.Span, Options)
                   ?? throw new InvalidDataException("The Communication envelope was empty.");
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"The Communication envelope is not valid schema-{SchemaVersion} JSON.", error);
        }
    }
}
