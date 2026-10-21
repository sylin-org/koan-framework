namespace Koan.Data.Abstractions.Failures;

/// <summary>
/// A write was refused because a value the Entity declares unique (<c>[Index(Unique = true)]</c>) is already in use
/// (DATA-0150).
///
/// <para>The message deliberately says nothing about where the value is held. Under row-scoped tenancy a unique index
/// is unique per tenant, but an existing database can still hold the global index an earlier version built, and the
/// store's own error names whichever index refused the value: reporting it would tell a caller whether another
/// tenant holds that value. The store's error is therefore not attached; <see cref="Failure"/> carries the stable,
/// redacted classification.</para>
/// </summary>
public sealed class UniqueValueConflictException : InvalidOperationException
{
    /// <summary>The <see cref="DataFailure.Code"/> an adapter's classifier gives a unique-value violation.</summary>
    public const string FailureCode = "unique-value";

    public UniqueValueConflictException(string entity, DataFailure failure)
        : base($"'{entity}' was not saved: a value it declares unique is already in use. Choose another value, or " +
               "reload the existing record and update it instead.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentNullException.ThrowIfNull(failure);
        Entity = entity;
        Failure = failure;
    }

    /// <summary>The Entity whose write was refused.</summary>
    public string Entity { get; }

    /// <summary>The adapter's classification: <see cref="DataFailureKind.Conflict"/>, never replayed.</summary>
    public DataFailure Failure { get; }
}
