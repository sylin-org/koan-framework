namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// One column, as the provider-neutral schema owner asks for it.
///
/// <para>Everything but <c>NativeType</c> is what the mapping <i>means</i>, and it reads the same on every
/// store. That is enough to decide what to create, and enough to notice a column that is missing or has
/// changed shape.</para>
///
/// <para><c>NativeType</c> is the store's own spelling when it has one — <c>varchar(255) CHARACTER SET
/// utf8mb4 COLLATE utf8mb4_0900_ai_ci</c>, say. It exists because the neutral fields cannot carry everything
/// worth validating: a character set is invisible to a CLR type, and MySQL was catching that drift before this
/// seam existed. Adopting a description that could not express it would have traded real validation for
/// uniformity. Both sides must supply it for it to be compared, so a store with no such notion is unaffected
/// rather than degraded.</para>
/// </summary>
public sealed record RelationalColumnDefinition(
    string Name,
    Type ClrType,
    bool Nullable,
    bool IsComputed = false,
    string? JsonPath = null,
    bool IsIndexed = false,
    RelationalStorageShape Shape = RelationalStorageShape.Scalar,
    bool IsIdentity = false,
    bool IsGenerated = false,
    string? NativeType = null);
