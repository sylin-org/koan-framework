using Koan.Data.Abstractions;

namespace Koan.Mcp;

/// <summary>
/// ARCH-0092 §H — the base of the MCP realization layer: a named bundle of <c>[McpTool]</c> verbs, the MCP
/// peer of an MVC controller (<c>Controller : actions :: Toolset : tools</c>). Standalone, non-entity tools
/// subclass this directly; entity CRUD/query verbs come from <see cref="EntityToolset{TEntity,TKey}"/>.
/// </summary>
public abstract class Toolset
{
}

/// <summary>
/// ARCH-0092 §H — the explicit MCP realization of <typeparamref name="TEntity"/>: exposes the entity's
/// endpoint verbs as MCP tools (over the shared <c>IEntityEndpointService</c>, exactly as
/// <c>EntityController&lt;TEntity&gt;</c> exposes them as REST actions) and hosts custom <c>[McpTool]</c>
/// instance verbs. An empty subclass exposes the entity with template descriptions — the same realization a
/// bare <c>[McpEntity]</c> produces; tune built-ins with <c>[ToolDescription]</c>/<c>[ToolHidden]</c>.
/// Built-in verbs are tune-only: their logic always runs the governed endpoint service (custom logic is a
/// custom <c>[McpTool]</c> verb).
/// </summary>
public abstract class EntityToolset<TEntity, TKey> : Toolset
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
}

/// <summary>Convenience for the common string-keyed entity, mirroring <c>EntityController&lt;TEntity&gt;</c>.</summary>
public abstract class EntityToolset<TEntity> : EntityToolset<TEntity, string>
    where TEntity : class, IEntity<string>
{
}
