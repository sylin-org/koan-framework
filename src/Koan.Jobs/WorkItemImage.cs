using Koan.Data.Abstractions;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Jobs;

/// <summary>
/// A work item as Koan Data persists it: its stored document, member by member (JOBS-0010). The document comes from the
/// same serializer and contract persistence writes with, so it holds exactly the members Koan stores — state behind a
/// non-public setter included, members the contract ignores excluded. The orchestrator takes one when it loads the item
/// for a job; at settle it compares the handler's copy against it to learn which members the handler changed, and the
/// stored row against it to learn which of those another writer changed meanwhile.
/// </summary>
internal sealed class WorkItemImage
{
    private readonly JObject _document;
    private readonly string _identity;

    private WorkItemImage(JObject document, string identity)
    {
        _document = document;
        _identity = identity;
    }

    /// <summary>The item's stored document, or null when the item cannot be serialized as Koan would store it.</summary>
    public static WorkItemImage? Capture(object item)
    {
        JObject document;
        try
        {
            document = EntityJsonSerialization.SerializeDocumentToken(item) as JObject
                ?? throw new JsonSerializationException($"'{item.GetType().FullName}' does not serialize to a JSON object.");
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return new WorkItemImage(document, IdentityMember(document));
    }

    /// <summary>The members of <paramref name="after"/> whose stored form differs from this image's.</summary>
    public List<string> ChangedIn(WorkItemImage after)
        => Members().Union(after.Members(), StringComparer.Ordinal)
            .Where(member => !Exact(this[member], after[member]))
            .ToList();

    /// <summary>The stored form of <paramref name="member"/>; null when the document does not hold it.</summary>
    public JToken? this[string member] => _document.TryGetValue(member, StringComparison.Ordinal, out var value) ? value : null;

    /// <summary>True when <paramref name="member"/> has the same stored form in both images.</summary>
    public bool Same(WorkItemImage other, string member) => Exact(this[member], other[member]);

    /// <summary>The same stored form, written out: a time at another offset of the same instant, or another kind of the
    /// same ticks, is another stored value (JSON.NET's own token equality compares times by instant).</summary>
    private static bool Exact(JToken? left, JToken? right)
        => left is null || right is null
            ? left is null && right is null
            : left.Type == right.Type
              && string.Equals(left.ToString(Formatting.None), right.ToString(Formatting.None), StringComparison.Ordinal);

    /// <summary>This document with <paramref name="members"/> taken from <paramref name="source"/>, materialized as
    /// <paramref name="type"/> through the serializer persistence restores with.</summary>
    public object With(WorkItemImage source, IEnumerable<string> members, Type type)
    {
        var merged = (JObject)_document.DeepClone();
        foreach (var member in members)
        {
            if (source[member] is { } value) merged[member] = value.DeepClone();
            else merged.Remove(member);
        }
        return EntityJsonSerialization.DeserializeDocument(merged.ToString(Formatting.None), type);
    }

    /// <summary>The members that make up the item's state: every stored member but its identity and the Entity-family
    /// type hint, which no handler changes.</summary>
    private IEnumerable<string> Members()
        => _document.Properties()
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, _identity, StringComparison.Ordinal)
                           && !string.Equals(name, EntityFamilyStorage.TypeField, StringComparison.Ordinal));

    /// <summary>The document member that holds the identity: the Entity's <c>Id</c> under the document's naming.</summary>
    private static string IdentityMember(JObject document)
        => document.Properties().Select(p => p.Name)
               .FirstOrDefault(name => string.Equals(name, nameof(IEntity<string>.Id), StringComparison.OrdinalIgnoreCase))
           ?? "id";
}
