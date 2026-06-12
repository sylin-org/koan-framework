namespace Koan.Web.Transformers.Tests.Fixtures;

/// <summary>
/// Plain POCO test entity. The registry only cares about the type as a dictionary key, so we don't
/// need a real <c>Entity&lt;&gt;</c> here.
/// </summary>
public sealed record Widget(string Id, string? Name = null, bool Enriched = false, bool AdminTagged = false);
