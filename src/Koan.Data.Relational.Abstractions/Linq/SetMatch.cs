namespace Koan.Data.Relational.Linq;

/// <summary>How a JSON array column meets a set bound as one value (<see cref="ILinqSqlDialect.CollectionMemberOf"/>).</summary>
public enum SetMatch
{
    /// <summary>Some element is a member (<c>HasAny</c>).</summary>
    Any,

    /// <summary>Every member is an element (<c>HasAll</c>).</summary>
    All,

    /// <summary>No element is a member (<c>HasNone</c>).</summary>
    None
}
