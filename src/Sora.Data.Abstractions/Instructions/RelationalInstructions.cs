namespace Sora.Data.Relational;

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
}
