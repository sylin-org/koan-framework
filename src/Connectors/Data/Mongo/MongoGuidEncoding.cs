using MongoDB.Bson;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// The single source of truth for how this connector encodes a scalar string/Guid on the wire:
/// a Guid-parseable string (and any <see cref="System.Guid"/>) is stored and queried as native UUID
/// BinData (<see cref="GuidRepresentation.Standard"/>); every other string stays a BSON string.
///
/// Both the write path (<c>SmartStringGuidSerializer</c>) and the query path
/// (<c>MongoFilterTranslator</c>) call this, so the two can never drift. They previously each made
/// the decision independently — the serializer on the runtime value, the translator on the declared
/// type — and drifted: a string-typed Guid FK wrote as BinData but queried as a BSON string, so the
/// predicate never matched and delete-when-empty callers silently lost data (DATA-XXXX).
/// </summary>
internal static class MongoGuidEncoding
{
    /// <summary>True when the string is persisted/queried as a UUID; yields the parsed Guid.</summary>
    public static bool IsGuidEncoded(string? value, out Guid guid) => Guid.TryParse(value, out guid);

    /// <summary>The canonical BinData representation for a Guid stored by this connector.</summary>
    public static BsonBinaryData ToBinData(Guid guid) => new(guid, GuidRepresentation.Standard);
}
