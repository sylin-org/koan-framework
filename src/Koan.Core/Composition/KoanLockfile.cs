using System.Collections.Generic;

namespace Koan.Core.Composition;

/// <summary>
/// The Koan composition lockfile schema (P1.1). A single shape covers BOTH emitters:
/// <list type="bullet">
/// <item>the build-time <c>koan.lock.json</c> (checked in) carries <see cref="Schema"/>,
/// <see cref="App"/>, <see cref="Modules"/>, and <see cref="DirectReferences"/> — everything else is runtime-resolved;</item>
/// <item>the boot-time resolved twin <c>obj/koan.lock.resolved.json</c> (gitignored) additionally
/// carries <see cref="Elections"/>, <see cref="ConfigKeys"/>, <see cref="Entities"/> and (best-effort)
/// <see cref="Capabilities"/>.</item>
/// </list>
/// Null sections are omitted on serialization, so the build-time file stays minimal. Config VALUES
/// never appear — only KEYS consumed (<see cref="ConfigKeys"/>).
/// </summary>
internal sealed record KoanLockfile(
    int Schema,
    KoanLockApp App,
    IReadOnlyList<KoanLockModule> Modules,
    IReadOnlyList<KoanLockReference>? DirectReferences = null,
    IReadOnlyDictionary<string, KoanLockElection>? Elections = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Capabilities = null,
    IReadOnlyList<string>? ConfigKeys = null,
    IReadOnlyList<KoanLockEntity>? Entities = null)
{
    /// <summary>The current schema version. Bump only on a breaking shape change.</summary>
    public const int CurrentSchema = 2;
}

/// <summary>Application identity. <see cref="Koan"/> is the framework's major.minor (breaking tier).</summary>
internal sealed record KoanLockApp(string Name, string Koan, string Tfm);

/// <summary>A referenced Koan module. <see cref="Version"/> is major.minor (see ARCH-0085 breaking tier).</summary>
internal sealed record KoanLockModule(string Id, string Version);

/// <summary>A direct application reference and its build origin.</summary>
internal sealed record KoanLockReference(string Kind, string Id);

/// <summary>A resolved election (e.g. <c>data:default</c>) — which provider won and why.</summary>
internal sealed record KoanLockElection(string Adapter, string Via, int? Priority = null);

/// <summary>An entity and the traits it declares (e.g. <c>Embedding</c>, <c>Cacheable</c>).</summary>
internal sealed record KoanLockEntity(string Type, IReadOnlyList<string> Traits);
