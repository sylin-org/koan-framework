using Koan.Data.Abstractions.Lexical;
using Koan.Data.Core.Model;

namespace Koan.Data.Core.Lexical;

/// <summary>
/// Type-scoped lexical search for one Entity kind: <c>Post.Lexical.Search("running shoes", s => s.Top(20))</c>.
/// Thin router over <see cref="Data{TEntity,TKey}"/>'s qualified lexical statics, present on every string-keyed
/// Entity. The index itself is declared with <c>[Lexical]</c> on the entity's string properties and kept by the
/// routed store in step with every write.
/// </summary>
public readonly struct LexicalStatics<T> where T : Entity<T>
{
    /// <summary>
    /// The ranked matches for <paramref name="text"/>: identities with scores, best first, and a snippet when the
    /// declaration asks for one. Every term is optional and a row holding more of them ranks higher. The declaration
    /// must bound the answer with <c>Top</c> or <c>Page</c>, and may narrow it with <c>Within</c> and read the text as
    /// still being typed with <c>AsTyped</c>.
    /// </summary>
    public Task<IReadOnlyList<LexicalMatch<string>>> Search(
        string text,
        Action<LexicalSearchQuery<T>> configure,
        CancellationToken ct = default)
    {
        var declaration = Declare(configure);
        return Data<T, string>.LexicalSearch(declaration.Build(text), declaration.Scope, ct);
    }

    /// <summary>The entities behind <see cref="Search"/>, loaded in rank order through the ordinary read path.</summary>
    public async Task<IReadOnlyList<T>> Find(
        string text,
        Action<LexicalSearchQuery<T>> configure,
        CancellationToken ct = default)
    {
        var declaration = Declare(configure);
        var matches = await Data<T, string>.LexicalSearch(declaration.Build(text), declaration.Scope, ct).ConfigureAwait(false);
        if (matches.Count == 0) return [];
        var loaded = await Data<T, string>.GetMany(matches.Select(static match => match.Id), ct).ConfigureAwait(false);
        return loaded.Where(static entity => entity is not null).ToArray()!;
    }

    /// <summary>
    /// Indexes every row of this kind, including rows that existed before the declaration, and answers how many
    /// rows the index now covers.
    /// </summary>
    public Task<long> Rebuild(CancellationToken ct = default) => Data<T, string>.LexicalRebuild(ct);

    /// <summary>What the routed store holds of this kind's lexical index now.</summary>
    public Task<LexicalIndexStatus> Status(CancellationToken ct = default) => Data<T, string>.LexicalStatus(ct);

    private static LexicalSearchQuery<T> Declare(Action<LexicalSearchQuery<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var declaration = new LexicalSearchQuery<T>();
        configure(declaration);
        return declaration;
    }
}

/// <summary>
/// Delivers the type-scoped <c>Post.Lexical</c> accessor as a C# 14 static extension member (ARCH-0135): every
/// string-keyed Entity kind gains it with <c>Sylin.Koan.Data.Core</c>, which every data connector carries. Thin
/// router over <see cref="Data{TEntity,TKey}"/>.
/// </summary>
public static class EntityLexicalExtensions
{
    extension<T>(T) where T : Entity<T>
    {
        /// <summary>Lexical search over this Entity kind's <c>[Lexical]</c> properties: search, find, rebuild, status.</summary>
        public static LexicalStatics<T> Lexical => default;
    }
}
