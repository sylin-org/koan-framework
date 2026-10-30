namespace Koan.Data.Abstractions.Instructions;

/// <summary>
/// Instruction name constants for relational adapters.
/// </summary>
public static class RelationalInstructions
{
    // Schema operations
    public const string SchemaValidate = "relational.schema.validate";
    public const string SchemaEnsureCreated = "relational.schema.ensureCreated";
    public const string SchemaClear = "relational.schema.clear";

    // Raw SQL operations
    public const string SqlScalar = "relational.sql.scalar";
    public const string SqlNonQuery = "relational.sql.nonquery";
    public const string SqlQuery = "relational.sql.query";

    /// <summary>
    /// <see cref="SqlQuery"/> option: the most rows the adapter reads from the command, as an <see cref="int"/>. Rows past
    /// it are never materialized (DATA-0162).
    /// </summary>
    public const string MaxRows = "relational.sql.maxRows";
}
