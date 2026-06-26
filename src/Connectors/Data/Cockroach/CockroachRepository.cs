using System;
using System.Diagnostics.CodeAnalysis;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Postgres;

namespace Koan.Data.Connector.Cockroach;

/// <summary>
/// CockroachDB repository — a <b>thin subclass</b> of <see cref="PostgresRepository{TEntity,TKey}"/> (ARCH-0094
/// Phase 4). CockroachDB speaks the PostgreSQL wire protocol (Npgsql) and nearly the same SQL, so the entire
/// repository — capabilities, <c>PgDialect</c>, the relational DDL, the managed-field AODB realization — is reused
/// wholesale. The Conformance Gate surfaced exactly one dialect delta on iteration 1: CockroachDB has no
/// <c>ctid</c> system column (error 42703), so the stable total-order fallback (<see cref="PostgresRepository{TEntity,TKey}.StableOrderClause"/>)
/// is overridden to order by the primary key — the only override this adapter needs.
/// </summary>
internal sealed class CockroachRepository<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties)] TEntity,
    TKey> : PostgresRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public CockroachRepository(IServiceProvider sp, CockroachOptions options, IStorageNameResolver resolver)
        : base(sp, ToPostgresOptions(options), resolver)
    {
    }

    /// <summary>CockroachDB has no <c>ctid</c> system column (42703); order by the primary key instead.</summary>
    protected override string StableOrderClause => "ORDER BY \"Id\"";

    // PostgresOptions is sealed, so CockroachOptions cannot subclass it — map the (identically-shaped, Postgres-enum)
    // Cockroach options onto the base options. The repository's behavior is otherwise byte-identical to Postgres.
    private static PostgresOptions ToPostgresOptions(CockroachOptions o) => new()
    {
        ConnectionString = o.ConnectionString,
        DefaultPageSize = o.DefaultPageSize,
        DdlPolicy = o.DdlPolicy,
        SchemaMatching = o.SchemaMatching,
        AllowProductionDdl = o.AllowProductionDdl,
        SearchPath = o.SearchPath,
        NamingStyle = o.NamingStyle,
        Separator = o.Separator,
        Readiness = o.Readiness,
    };
}
