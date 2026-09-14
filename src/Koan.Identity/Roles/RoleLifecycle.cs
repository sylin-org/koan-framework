namespace Koan.Identity.Roles;

public enum RoleChangePhase { Before, After }
public sealed record RoleChangeDecision(bool Allowed, string Code, string Message)
{
    public static RoleChangeDecision Continue() => new(true, "role.change.allowed", "The role change may continue.");
    public static RoleChangeDecision Veto(string code, string message) => new(false, code, message);
}

public sealed record RoleChangeContext(Role Previous, Role Current, string? Subject, string? Actor,
    RoleChangePhase Phase, CancellationToken CancellationToken);

public sealed class RolePostEventException(string message, Exception innerException) : Exception(message, innerException);

public sealed class RoleLifecycleBuilder
{
    public RoleLifecycleBuilder MemberAdding(Func<RoleChangeContext, ValueTask<RoleChangeDecision>> h) => Add(RoleEventKind.MemberAdding, h);
    public RoleLifecycleBuilder MemberAdded(Func<RoleChangeContext, ValueTask> h) => Add(RoleEventKind.MemberAdded, h);
    public RoleLifecycleBuilder MemberRemoving(Func<RoleChangeContext, ValueTask<RoleChangeDecision>> h) => Add(RoleEventKind.MemberRemoving, h);
    public RoleLifecycleBuilder MemberRemoved(Func<RoleChangeContext, ValueTask> h) => Add(RoleEventKind.MemberRemoved, h);
    public RoleLifecycleBuilder PermissionsChanging(Func<RoleChangeContext, ValueTask<RoleChangeDecision>> h) => Add(RoleEventKind.PermissionsChanging, h);
    public RoleLifecycleBuilder PermissionsChanged(Func<RoleChangeContext, ValueTask> h) => Add(RoleEventKind.PermissionsChanged, h);
    public RoleLifecycleBuilder RoleChanging(Func<RoleChangeContext, ValueTask<RoleChangeDecision>> h) => Add(RoleEventKind.RoleChanging, h);
    public RoleLifecycleBuilder RoleChanged(Func<RoleChangeContext, ValueTask> h) => Add(RoleEventKind.RoleChanged, h);
    public RoleLifecycleBuilder RoleDeleting(Func<RoleChangeContext, ValueTask<RoleChangeDecision>> h) => Add(RoleEventKind.RoleDeleting, h);
    public RoleLifecycleBuilder RoleDeleted(Func<RoleChangeContext, ValueTask> h) => Add(RoleEventKind.RoleDeleted, h);
    public RoleLifecycleBuilder Reset() { RoleEventRegistry.Reset(); return this; }
    private static RoleLifecycleBuilder Add(RoleEventKind kind, Delegate handler) { ArgumentNullException.ThrowIfNull(handler); RoleEventRegistry.Add(kind, handler); return new(); }
}

internal enum RoleEventKind { MemberAdding, MemberAdded, MemberRemoving, MemberRemoved, PermissionsChanging, PermissionsChanged, RoleChanging, RoleChanged, RoleDeleting, RoleDeleted }
internal static class RoleEventRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<RoleEventKind, Delegate[]> Handlers = [];
    private static readonly AsyncLocal<bool> Dispatching = new();
    public static void Add(RoleEventKind kind, Delegate handler) { lock (Gate) Handlers[kind] = Handlers.TryGetValue(kind, out var h) ? [.. h, handler] : [handler]; }
    public static void Reset() { lock (Gate) Handlers.Clear(); }
    public static void DemandMutationAllowed() { if (Dispatching.Value) throw new InvalidOperationException("Recursive role mutation from a role lifecycle handler is not supported."); }
    public static async ValueTask Before(RoleEventKind kind, RoleChangeContext context)
    {
        DemandMutationAllowed(); var handlers = Snapshot(kind); Dispatching.Value = true;
        try { foreach (var h in handlers.Cast<Func<RoleChangeContext, ValueTask<RoleChangeDecision>>>()) { var d = await h(context); if (!d.Allowed) throw new UnauthorizedAccessException($"{d.Code}: {d.Message}"); } }
        finally { Dispatching.Value = false; }
    }
    public static async ValueTask After(RoleEventKind kind, RoleChangeContext context)
    {
        DemandMutationAllowed(); var handlers = Snapshot(kind); Dispatching.Value = true;
        try { foreach (var h in handlers.Cast<Func<RoleChangeContext, ValueTask>>()) await h(context); }
        catch (Exception e) { throw new RolePostEventException("The role mutation committed and its bags were invalidated, but a post-change handler failed.", e); }
        finally { Dispatching.Value = false; }
    }
    private static Delegate[] Snapshot(RoleEventKind kind) { lock (Gate) return Handlers.TryGetValue(kind, out var h) ? h : []; }
}
