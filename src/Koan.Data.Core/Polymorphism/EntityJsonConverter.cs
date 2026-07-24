using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Core.Polymorphism;

/// <summary>Restrictive read converter for Entity-family JSON documents.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class EntityJsonConverter : JsonConverter
{
    private static readonly AsyncLocal<BypassFrame?> CurrentBypass = new();

    public static EntityJsonConverter Instance { get; } = new();

    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
    {
        if (TryConsumeBypass(objectType) ||
            !EntityRootDescriptor.TryFor(objectType, out var descriptor))
        {
            return false;
        }

        return descriptor.IsVariant ||
               EntityTypeCatalog.HasVariants(descriptor.RootType) ||
               EntityMaterializationScope.TargetFor(descriptor.RootType) is not null;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotSupportedException("EntityJsonConverter is read-only.");

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var document = JObject.Load(reader);
        return EntityJsonSerialization.Materialize(document, objectType, serializer);
    }

    internal static IDisposable BypassOnce(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        var prior = CurrentBypass.Value;
        var frame = new BypassFrame(objectType);
        CurrentBypass.Value = frame;
        return new BypassLease(frame, prior);
    }

    private static bool TryConsumeBypass(Type objectType)
    {
        var frame = CurrentBypass.Value;
        if (frame is null || frame.Consumed || frame.ObjectType != objectType)
        {
            return false;
        }

        frame.Consumed = true;
        return true;
    }

    private sealed class BypassFrame(Type objectType)
    {
        public Type ObjectType { get; } = objectType;
        public bool Consumed { get; set; }
    }

    private sealed class BypassLease(BypassFrame frame, BypassFrame? prior) : IDisposable
    {
        private BypassFrame? _frame = frame;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _frame, null);
            if (current is null)
            {
                return;
            }

            if (!ReferenceEquals(CurrentBypass.Value, current))
            {
                throw new InvalidOperationException(
                    "Entity JSON converter bypasses must be disposed in reverse order.");
            }

            CurrentBypass.Value = prior;
        }
    }
}
