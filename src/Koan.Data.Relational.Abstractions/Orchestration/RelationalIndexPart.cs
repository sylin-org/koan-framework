using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// One indexed value, carried in the terms an adapter's own dialect already speaks.
///
/// <para><see cref="PhysicalType"/> travels with the path because a nested value's index expression has to be
/// the expression queries emit, character for character, or the store's planner will never choose the index.
/// Dialects derive that expression from both — SQLite quotes each path segment and casts numerics — so a part
/// carrying only a rendered JSON path would build an index that looks right and is never used.</para>
/// </summary>
public sealed record RelationalIndexPart(PhysicalPath Path, Type PhysicalType, string EncodingId);
