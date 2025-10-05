using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core;
using Koan.Canon.Runtime;
using Koan.Canon.Options;
using Koan.Canon.Model;
using Koan.Data.Core;
using System.Reflection;

namespace Koan.Canon.Runtime.Connector.Dapr;

internal sealed class DaprCanonRuntime : ICanonRuntime
{
    private readonly ILogger<DaprCanonRuntime> _log;
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<CanonOptions> _opts;

    public DaprCanonRuntime(ILogger<DaprCanonRuntime> log, IServiceProvider sp, IOptionsMonitor<CanonOptions> opts)
    { _log = log; _sp = sp; _opts = opts; }

    public Task StartAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Koan.Canon Dapr runtime active");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    { _log.LogInformation("Koan.Canon Dapr runtime stopped."); return Task.CompletedTask; }

    public async Task ReplayAsync(DateTimeOffset? from = null, DateTimeOffset? until = null, CancellationToken ct = default)
    {
        // Minimal behavior: enqueue projection tasks for references marked as RequiresProjection
        // Future: respect time windows using lineage/timestamps when available.
        var viewName = _opts.CurrentValue.DefaultViewName;
        _log.LogInformation("[Dapr] Replay enqueuing tasks for references requiring projection. View={View}", viewName);

        // Discover legacy-free typed models and enqueue tasks per model
        foreach (var modelType in DiscoverModels())
        {
            var riType = typeof(ReferenceItem<>).MakeGenericType(modelType);
            var query = typeof(Koan.Data.Core.Data<,>).MakeGenericType(riType, typeof(string)).GetMethod("Query", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
            var task = (System.Threading.Tasks.Task)query.Invoke(null, new object?[] { "RequiresProjection == true", ct })!; await task.ConfigureAwait(false);
            var list = (System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
            foreach (var item in list)
            {
                if (ct.IsCancellationRequested) break;
                // Use ULID (Id) as the reference identifier
                var refId = (string)item.GetType().GetProperty("Id")!.GetValue(item)!;
                var ver = (ulong)item.GetType().GetProperty("Version")!.GetValue(item)!;
                await EnqueueIfMissing(refId, ver, viewName, ct);
            }
        }
    }

    public async Task ReprojectAsync(string referenceId, string? viewName = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(referenceId)) return;
        var vn = string.IsNullOrWhiteSpace(viewName) ? _opts.CurrentValue.DefaultViewName : viewName!;
        // Resolve latest version via typed ReferenceItem<T>
        foreach (var modelType in DiscoverModels())
        {
            var riType = typeof(ReferenceItem<>).MakeGenericType(modelType);
            var getM = typeof(Koan.Data.Core.Data<,>).MakeGenericType(riType, typeof(string)).GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
            var task = (System.Threading.Tasks.Task)getM.Invoke(null, new object?[] { referenceId, ct })!; await task.ConfigureAwait(false);
            var refItem = task.GetType().GetProperty("Result")!.GetValue(task);
            if (refItem is null) continue;
            var ver = (ulong)refItem.GetType().GetProperty("Version")!.GetValue(refItem)!;
            await EnqueueIfMissing(referenceId, ver, vn, ct);
            _log.LogInformation("[Dapr] Reproject enqueued for {ReferenceId} v{Version} view={View}", referenceId, ver, vn);
            return;
        }
        _log.LogDebug("[Dapr] Reproject: reference not found {ReferenceId}", referenceId);
    }

    private static string TaskKey(string referenceId, ulong version, string viewName)
        => $"{referenceId}::{version}::{viewName}";

    private async Task EnqueueIfMissing(string referenceId, ulong version, string viewName, CancellationToken ct)
    {
        // Look for an existing task; if not found, create it.
        bool exists = false;
        var taskId = TaskKey(referenceId, version, viewName);
        foreach (var modelType in DiscoverModels())
        {
            var ptType = typeof(ProjectionTask<>).MakeGenericType(modelType);
            var get = typeof(Koan.Data.Core.Data<,>).MakeGenericType(ptType, typeof(string)).GetMethod("GetAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
            var t = (System.Threading.Tasks.Task)get.Invoke(null, new object?[] { taskId, ct })!; await t.ConfigureAwait(false);
            var found = t.GetType().GetProperty("Result")!.GetValue(t);
            if (found is not null) { exists = true; break; }
        }
        if (exists) return;
        // create in all models (harmless; only correct model base will be used for naming)
        foreach (var modelType in DiscoverModels())
        {
            var ptType = typeof(ProjectionTask<>).MakeGenericType(modelType);
            var entity = Activator.CreateInstance(ptType)!;
            ptType.GetProperty("Id")!.SetValue(entity, taskId);
            ptType.GetProperty("ReferenceId")?.SetValue(entity, referenceId);
            ptType.GetProperty("Version")!.SetValue(entity, version);
            ptType.GetProperty("ViewName")!.SetValue(entity, viewName);
            ptType.GetProperty("CreatedAt")!.SetValue(entity, DateTimeOffset.UtcNow);
            var upsert = typeof(Koan.Data.Core.Data<,>).MakeGenericType(ptType, typeof(string)).GetMethod("UpsertAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, new[] { ptType, typeof(CancellationToken) })!;
            await (System.Threading.Tasks.Task)upsert.Invoke(null, new object?[] { entity, ct })!;
        }
    }

    private static List<Type> DiscoverModels()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                if (bt.GetGenericTypeDefinition() != typeof(Koan.Canon.Model.CanonEntity<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }
}





