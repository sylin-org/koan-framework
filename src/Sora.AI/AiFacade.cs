using Microsoft.Extensions.DependencyInjection;
using Sora.AI.Contracts;
using Sora.AI.Contracts.Models;
using Sora.Core;
using System.Runtime.CompilerServices;

namespace Sora.AI;

public static class Ai
{
    private static readonly AsyncLocal<IAi?> _override = new();
    private static Func<IServiceProvider, IAi>? _resolver;

    public static IDisposable With(IAi @override)
    {
        var prev = _override.Value;
        _override.Value = @override;
        return new Reset(() => _override.Value = prev);
    }

    public static Task<AiChatResponse> Prompt(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Resolve().PromptAsync(BuildChat(message, model, opts), ct);

    public static IAsyncEnumerable<AiChatChunk> Stream(string message, string? model = null, AiPromptOptions? opts = null, CancellationToken ct = default)
        => Resolve().StreamAsync(BuildChat(message, model, opts), ct);

    public static Task<AiEmbeddingsResponse> Embed(AiEmbeddingsRequest req, CancellationToken ct = default)
        => Resolve().EmbedAsync(req, ct);

    // Discovery helpers for optional usage
    public static bool IsAvailable
    {
        get
        {
            if (_override.Value is IAi) return true;
            var sp = SoraApp.Current;
            if (sp is null) return false;
            var ia = sp.GetService<IAi>();
            if (ia is not null) return true;
            var scopeFactory = sp.GetService<IServiceScopeFactory>();
            if (scopeFactory is null) return false;
            using var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetService<IAi>() is not null;
        }
    }

    public static IAi? TryResolve()
    {
        if (_override.Value is IAi o) return o;
        var sp = SoraApp.Current;
        if (sp is null) return null;
        var ia = sp.GetService<IAi>();
        if (ia is not null) return ia;
        var scopeFactory = sp.GetService<IServiceScopeFactory>();
        if (scopeFactory is null) return null;
        using var scope = scopeFactory.CreateScope();
        return scope.ServiceProvider.GetService<IAi>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IAi Resolve()
    {
        if (_override.Value is IAi o) return o;
        var sp = SoraApp.Current ?? throw new InvalidOperationException("AI not configured; call services.AddSora() or AddAi() and ensure provider.UseSora().");
        _resolver ??= CreateResolver(sp);
        return _resolver(sp);
    }

    private static AiChatRequest BuildChat(string message, string? model, AiPromptOptions? opts)
        => new()
        {
            Messages = new() { new AiMessage("user", message) },
            Model = model,
            Options = opts
        };

    private static Func<IServiceProvider, IAi> CreateResolver(IServiceProvider sp)
    {
        // Cache the delegate per ServiceProvider instance
        return (svc) =>
        {
            var scopeFactory = svc.GetService<IServiceScopeFactory>();
            if (scopeFactory is null)
                throw new InvalidOperationException("ServiceScopeFactory missing; invalid DI container state.");
            // Prefer ambient scope if present (ASP.NET); else create a scope
            var ia = svc.GetService<IAi>();
            if (ia is not null) return ia;
            using var scope = scopeFactory.CreateScope();
            ia = scope.ServiceProvider.GetService<IAi>();
            return ia ?? throw new InvalidOperationException("IAi not registered; call AddSora() or AddAi().");
        };
    }

    private sealed class Reset : IDisposable
    {
        private readonly Action _onDispose;
        public Reset(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}
