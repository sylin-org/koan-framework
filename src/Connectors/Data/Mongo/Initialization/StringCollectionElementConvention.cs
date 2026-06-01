using System.Collections;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Koan.Data.Connector.Mongo.Initialization;

/// <summary>
/// GUID carve-out for collection elements (DATA-XXXX locked decision). The global
/// <see cref="SmartStringGuidSerializer"/> is registered for <c>typeof(string)</c>, so it would
/// rewrite GUID-shaped strings to BinData EVERYWHERE — including the elements of a
/// <c>List&lt;string&gt;</c> / <c>string[]</c> stored as a JSON/BSON array. That corrupts
/// List&lt;string&gt; round-trips and breaks the array-containment operators (Has/HasAny/HasAll/
/// HasNone), whose element values the Mongo translator emits as BSON strings.
///
/// This member-map convention detects members whose type is an enumerable of <c>string</c>
/// (excluding <c>string</c> itself) and forces an array serializer whose ELEMENT serializer is the
/// plain <see cref="StringSerializer"/> — so array elements are always stored and compared as BSON
/// strings, regardless of the global smart serializer. Scalar string members (including the Guid-
/// backed Id) keep the smart serializer and its UUID/BinData optimization.
/// </summary>
internal sealed class StringCollectionElementConvention : ConventionBase, IMemberMapConvention
{
    public void Apply(BsonMemberMap memberMap)
    {
        var memberType = memberMap.MemberType;
        var elementType = TryGetEnumerableElementType(memberType);
        if (elementType != typeof(string)) return;

        var stringElementSerializer = new StringSerializer();

        if (memberType.IsArray)
        {
            memberMap.SetSerializer(new ArraySerializer<string>(stringElementSerializer));
            return;
        }

        // List<string>, IList<string>, ICollection<string>, IEnumerable<string>, etc.
        if (memberType.IsGenericType)
        {
            var def = memberType.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) ||
                def == typeof(ICollection<>) || def == typeof(IReadOnlyList<>) ||
                def == typeof(IReadOnlyCollection<>) || def == typeof(IEnumerable<>))
            {
                memberMap.SetSerializer(
                    new EnumerableInterfaceImplementerSerializer<List<string>, string>(stringElementSerializer));
            }
        }
    }

    /// <summary>Element type when the member is an enumerable (excluding string), else null.</summary>
    private static Type? TryGetEnumerableElementType(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();
        if (!typeof(IEnumerable).IsAssignableFrom(t)) return null;

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            return t.GetGenericArguments()[0];

        foreach (var iface in t.GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];

        return null;
    }
}
