using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// One column, as the provider-neutral schema owner asks for it.
///
/// <para>Everything here is what the mapping <i>means</i>, and it reads the same on every store. That is enough
/// to decide what to create, and enough to notice a column that is missing or has changed shape. What a store
/// actually holds is a different thing, described by <see cref="RelationalColumnState"/>.</para>
///
/// <para>Nullability is not here. Every store already derives it from identity and none of them agreed on the
/// rest - SQLite constrains only its key, PostgreSQL constrains everything, SQL Server and MySQL constrain
/// nothing else - so a single neutral answer could only have been wrong for three of the four, and comparing
/// against it invented drift on the one store that checks. Whether a column accepts an absent value is a store
/// convention, created and compared through <see cref="IRelationalDdlExecutor.ColumnMatches"/>.</para>
///
/// <para><c>IsProjected</c> marks a column the store derives from the structured root instead of accepting from
/// a writer - SQL Server's <c>PERSISTED</c> computed column, MySQL's <c>STORED</c> generated column.
/// <c>ProjectedFrom</c> is the value it reads, carried as a path rather than a rendered expression so the
/// executor can hand it to its own dialect: a computed column whose spelling differs by a character from the
/// reads it exists to serve is one no planner will use.</para>
/// </summary>
public sealed record RelationalColumnDefinition
{
    public RelationalColumnDefinition(
        string Name,
        Type ClrType,
        RelationalStorageShape Shape = RelationalStorageShape.Scalar,
        bool IsIdentity = false,
        bool IsGenerated = false,
        bool IsProjected = false,
        PhysicalPath? ProjectedFrom = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(ClrType);
        if (ProjectedFrom is not null && !IsProjected)
            throw new ArgumentException(
                $"Column '{Name}' names a projection source but is not marked projected.", nameof(ProjectedFrom));
        this.Name = Name;
        this.ClrType = ClrType;
        this.Shape = Shape;
        this.IsIdentity = IsIdentity;
        this.IsGenerated = IsGenerated;
        this.IsProjected = IsProjected;
        this.ProjectedFrom = ProjectedFrom;
    }

    public string Name { get; init; }
    public Type ClrType { get; init; }
    public RelationalStorageShape Shape { get; init; }

    /// <summary>Whether this column carries part of the mapping's identity.</summary>
    public bool IsIdentity { get; init; }

    /// <summary>Whether the store supplies the value on insert - an auto-increment or sequence identity.</summary>
    public bool IsGenerated { get; init; }

    /// <summary>Whether the store computes the value from the structured root on every write.</summary>
    public bool IsProjected { get; init; }

    /// <summary>The value a projected column reads.</summary>
    public PhysicalPath? ProjectedFrom { get; init; }
}
