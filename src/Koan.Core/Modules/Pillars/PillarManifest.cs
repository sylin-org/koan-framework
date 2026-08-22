using System.Threading;

namespace Koan.Core.Modules.Pillars;

/// <summary>
/// One pillar's declaration — what it is called, how it is shown, and which namespaces belong to it — and the
/// registration that follows from it.
///
/// <para>Six assemblies declare a pillar, and until 2026-08-21 each carried its own copy of this: the same
/// latch, the same double-checked lock, the same registration and the same namespace loop, differing only in
/// four constants and a list of roots. Six copies of a registration are six places for a typo that nothing
/// checks, and one of them locked on its own public type, which anyone outside could take.</para>
///
/// <para>A root is written once, without a trailing dot, and claims both the assembly of that name and
/// everything beneath it. Every manifest used to register two spellings — <c>Koan.Data</c> and
/// <c>Koan.Data.</c> — and the second was doing nothing: matching is a longest-prefix <c>StartsWith</c>, so the
/// bare root already covers the dotted case. Measured by disabling each in turn: without the bare form the
/// assembly of the root's own name stops resolving, and without the dotted form nothing changes at all. Where
/// two pillars overlap, the longer root wins, which is what <c>Koan.Web</c> and <c>Koan.Web.Auth</c> rely
/// on.</para>
/// </summary>
public sealed class PillarManifest
{
    private readonly string _label;
    private readonly string _colorHex;
    private readonly string _icon;
    private readonly string[] _roots;
    private readonly object _sync = new();
    private int _registered;

    /// <param name="code">The pillar identifier, as provenance and reporting spell it.</param>
    /// <param name="label">Its display name.</param>
    /// <param name="colorHex">Its display colour.</param>
    /// <param name="icon">Its display icon.</param>
    /// <param name="roots">Namespace roots this pillar owns, without a trailing dot.</param>
    public PillarManifest(string code, string label, string colorHex, string icon, params string[] roots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        _label = label;
        _colorHex = colorHex;
        _icon = icon;
        _roots = roots ?? [];
    }

    public string Code { get; }

    /// <summary>Declares this pillar once per process. Safe to call from anywhere, as often as you like.</summary>
    public void EnsureRegistered()
    {
        if (Volatile.Read(ref _registered) == 1) return;

        lock (_sync)
        {
            if (_registered == 1) return;

            KoanPillarCatalog.RegisterDescriptor(
                new KoanPillarCatalog.PillarDescriptor(Code, _label, _colorHex, _icon));

            foreach (var root in _roots)
            {
                KoanPillarCatalog.AssociateNamespace(Code, root);
            }

            Volatile.Write(ref _registered, 1);
        }
    }

    public KoanPillarCatalog.PillarDescriptor Descriptor
    {
        get
        {
            EnsureRegistered();
            return KoanPillarCatalog.RequireByCode(Code);
        }
    }
}
