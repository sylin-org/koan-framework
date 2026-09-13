namespace Koan.Identity.Roles;

public sealed class ScopedRoleLifecycleBuilder
{
    public ScopedRoleLifecycleBuilder MemberAdding(Func<ScopedRoleChangeContext, ValueTask<ScopedRoleChangeDecision>> handler)
        => Add(ScopedRoleEventKind.MemberAdding, handler);
    public ScopedRoleLifecycleBuilder MemberAdded(Func<ScopedRoleChangeContext, ValueTask> handler)
        => Add(ScopedRoleEventKind.MemberAdded, handler);
    public ScopedRoleLifecycleBuilder MemberRemoving(Func<ScopedRoleChangeContext, ValueTask<ScopedRoleChangeDecision>> handler)
        => Add(ScopedRoleEventKind.MemberRemoving, handler);
    public ScopedRoleLifecycleBuilder MemberRemoved(Func<ScopedRoleChangeContext, ValueTask> handler)
        => Add(ScopedRoleEventKind.MemberRemoved, handler);
    public ScopedRoleLifecycleBuilder PermissionsChanging(Func<ScopedRoleChangeContext, ValueTask<ScopedRoleChangeDecision>> handler)
        => Add(ScopedRoleEventKind.PermissionsChanging, handler);
    public ScopedRoleLifecycleBuilder PermissionsChanged(Func<ScopedRoleChangeContext, ValueTask> handler)
        => Add(ScopedRoleEventKind.PermissionsChanged, handler);
    public ScopedRoleLifecycleBuilder Reset() { ScopedRoleEventRegistry.Reset(); return this; }

    private static ScopedRoleLifecycleBuilder Add(ScopedRoleEventKind kind, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ScopedRoleEventRegistry.Add(kind, handler);
        return new();
    }
}

internal enum ScopedRoleEventKind
{
    MemberAdding, MemberAdded, MemberRemoving, MemberRemoved, PermissionsChanging, PermissionsChanged,
}

internal static class ScopedRoleEventRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<ScopedRoleEventKind, Delegate[]> Handlers = [];
    private static readonly AsyncLocal<bool> Dispatching = new();

    public static void Add(ScopedRoleEventKind kind, Delegate handler)
    {
        lock (Gate) Handlers[kind] = Handlers.TryGetValue(kind, out var current) ? [.. current, handler] : [handler];
    }

    public static void Reset() { lock (Gate) Handlers.Clear(); }

    public static void DemandMutationAllowed()
    {
        if (Dispatching.Value)
            throw new ScopedRoleValidationException("role.event.recursion",
                "Recursive role mutation from a role lifecycle handler is not supported.");
    }

    public static async ValueTask Before(ScopedRoleEventKind kind, ScopedRoleChangeContext context)
    {
        if (Dispatching.Value) throw new ScopedRoleValidationException("role.event.recursion", "Recursive role mutation from a role lifecycle handler is not supported.");
        var handlers = Snapshot(kind);
        try
        {
            Dispatching.Value = true;
            foreach (var handler in handlers.Cast<Func<ScopedRoleChangeContext, ValueTask<ScopedRoleChangeDecision>>>())
            {
                var decision = await handler(context).ConfigureAwait(false);
                if (!decision.Allowed) throw new ScopedRoleAuthorizationException(decision.Code, decision.Message);
            }
        }
        finally { Dispatching.Value = false; }
    }

    public static async ValueTask After(ScopedRoleEventKind kind, ScopedRoleChangeContext context)
    {
        if (Dispatching.Value) throw new ScopedRoleValidationException("role.event.recursion", "Recursive role mutation from a role lifecycle handler is not supported.");
        var handlers = Snapshot(kind);
        try
        {
            Dispatching.Value = true;
            foreach (var handler in handlers.Cast<Func<ScopedRoleChangeContext, ValueTask>>())
                await handler(context).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not ScopedRolePostEventException)
        {
            throw new ScopedRolePostEventException("The role mutation committed and cache invalidation completed, but a post-change handler failed.", error);
        }
        finally { Dispatching.Value = false; }
    }

    private static Delegate[] Snapshot(ScopedRoleEventKind kind)
    {
        lock (Gate) return Handlers.TryGetValue(kind, out var handlers) ? handlers : [];
    }
}
