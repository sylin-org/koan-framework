using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow;
using Sora.Flow.Materialization;
using System.Reflection;
using Sora.Messaging;

namespace Sora.Flow.Web.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Web";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure MVC sees controllers from this assembly
    var mvc = services.AddControllers();
    mvc.AddApplicationPart(typeof(SoraAutoRegistrar).Assembly);

        // Discover Flow models and register FlowEntityController<TModel> under /api/flow/{model}
        foreach (var modelType in DiscoverModels())
        {
            var modelName = FlowRegistry.GetModelName(modelType);
            var route = $"{Sora.Flow.Web.Infrastructure.WebConstants.Routes.DefaultPrefix}/{modelName}";
            // Register FlowEntityController<TModel> bound to this model type via GenericControllers helper (by reflection)
            var gcType = Type.GetType("Sora.Web.Extensions.GenericControllers.GenericControllers, Sora.Web.Extensions");
            if (gcType is not null)
            {
                var addGeneric = gcType.GetMethod("AddGenericController", BindingFlags.Public | BindingFlags.Static);
                if (addGeneric is not null)
                {
                    var g = addGeneric.MakeGenericMethod(modelType);
                    _ = g.Invoke(null, new object?[] { services, typeof(Sora.Flow.Web.Controllers.FlowEntityController<>), route });
                }
            }
        }
        // Discover Flow value-objects and register standard EntityController<TVo> under /api/vo/{type}
        foreach (var voType in DiscoverValueObjects())
        {
            var voName = FlowRegistry.GetModelName(voType);
            var route = $"/api/vo/{voName}";
            var gcType = Type.GetType("Sora.Web.Extensions.GenericControllers.GenericControllers, Sora.Web.Extensions");
            if (gcType is not null)
            {
                var addGeneric = gcType.GetMethod("AddGenericController", BindingFlags.Public | BindingFlags.Static);
                if (addGeneric is not null)
                {
                    var g = addGeneric.MakeGenericMethod(voType);
                    _ = g.Invoke(null, new object?[] { services, typeof(Sora.Web.Controllers.EntityController<>), route });
                }
            }
        }
        // Health/metrics are assumed to be added by host; controllers expose endpoints only.

    // Opt-out turnkey: auto-add Flow runtime in web hosts unless disabled via config.
        // Gate: Sora:Flow:AutoRegister (default: true). Idempotent: skips if already added.
        try
        {
            IConfiguration? cfg = null;
            try
            {
                var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
                cfg = existing?.ImplementationInstance as IConfiguration;
            }
            catch { }

            var enabled = true; // default ON
            if (cfg is not null)
            {
                // Prefer explicit boolean; treat missing as true
                var val = cfg.GetValue<bool?>("Sora:Flow:AutoRegister");
                if (val.HasValue) enabled = val.Value;
            }

            // Skip if Flow already wired (presence of IFlowMaterializer indicates AddSoraFlow ran)
            var already = services.Any(d => d.ServiceType == typeof(IFlowMaterializer));
            if (enabled && !already)
            {
                services.AddSoraFlow();
            }
        }
        catch { }

    // API-side responder for announce control commands (turnkey ON, opt-out via config)
    services.OnMessages(h => h.On<Sora.Flow.Model.ControlCommand>(async (env, cmd, ct) =>
        {
            try
            {
                // Check gate
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var cfg = sp?.GetService(typeof(IConfiguration)) as IConfiguration;
                var announceEnabled = cfg?.GetValue<bool?>(Sora.Flow.Web.Infrastructure.WebConstants.Control.Config.AnnounceEnabled) ?? true;
                var pingEnabled = cfg?.GetValue<bool?>(Sora.Flow.Web.Infrastructure.WebConstants.Control.Config.PingEnabled) ?? true;

                var verbStr = (cmd.Verb ?? string.Empty).Trim().ToLowerInvariant();
                if (verbStr == Sora.Flow.Web.Infrastructure.WebConstants.Control.Verbs.Ping)
                {
                    if (!pingEnabled) return;
                    var reference = cmd.Target ?? string.Empty;
                    // If a target is provided, verify existence in registry; otherwise general pong
                    var pingTarget = (cmd.Target ?? cmd.Arg ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(pingTarget))
                    {
                        await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, reference, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Ok, null, env.CorrelationId).Send(ct);
                        return;
                    }
                    if (!pingTarget.Contains(':'))
                    {
                        await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, reference, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Unsupported, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.TargetRequired, env.CorrelationId).Send(ct);
                        return;
                    }
                    var pingParts = pingTarget.Split(':', 2);
                    var pingSys = pingParts[0];
                    var pingAdp = pingParts[1];
                    var pingReg = (Sora.Flow.Monitoring.IAdapterRegistry?)sp?.GetService(typeof(Sora.Flow.Monitoring.IAdapterRegistry));
                    if (pingReg is null)
                    {
                        await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, reference, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Error, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.RegistryUnavailable, env.CorrelationId).Send(ct);
                        return;
                    }
                    var pingExists = pingReg.All().Any(x => string.Equals(x.System, pingSys, StringComparison.OrdinalIgnoreCase)
                                                  && (pingAdp == "*" || string.Equals(x.Adapter, pingAdp, StringComparison.OrdinalIgnoreCase)));
                    if (!pingExists)
                    {
                        await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, reference, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.NotFound, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.NoAdapterMatched(pingTarget), env.CorrelationId).Send(ct);
                        return;
                    }
                    await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, reference, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Ok, null, env.CorrelationId).Send(ct);
                    return;
                }
                if (verbStr != Sora.Flow.Web.Infrastructure.WebConstants.Control.Verbs.Announce) return;

                var target = (cmd.Target ?? cmd.Arg ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(target) || !target.Contains(':'))
                {
                    await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, string.Empty, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Unsupported, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.TargetRequired, env.CorrelationId).Send(ct);
                    return;
                }
                var parts = target.Split(':', 2);
                var sys = parts[0];
                var adp = parts[1];

                var reg = (Sora.Flow.Monitoring.IAdapterRegistry?)sp?.GetService(typeof(Sora.Flow.Monitoring.IAdapterRegistry));
                if (reg is null)
                {
                    await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, string.Empty, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Error, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.RegistryUnavailable, env.CorrelationId).Send(ct);
                    return;
                }

                var items = reg.All()
                    .Where(x => string.Equals(x.System, sys, StringComparison.OrdinalIgnoreCase)
                             && (adp == "*" || string.Equals(x.Adapter, adp, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(x => x.LastSeenAt)
                    .ToList();
                var found = items.FirstOrDefault();
                if (found is null)
                {
                    await new Sora.Flow.Actions.FlowAck(Sora.Flow.Web.Infrastructure.WebConstants.Control.Model, verbStr, string.Empty, Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.NotFound, Sora.Flow.Web.Infrastructure.WebConstants.Control.Messages.NoAdapterMatched(target), env.CorrelationId).Send(ct);
                    return;
                }

                var payload = new Sora.Flow.Model.AdapterAnnouncement
                {
                    System = found.System,
                    Adapter = found.Adapter,
                    InstanceId = found.InstanceId,
                    Version = found.Version,
                    Capabilities = found.Capabilities,
                    Bus = found.Bus,
                    Group = found.Group,
                    Host = found.Host,
                    Pid = found.Pid,
                    StartedAt = found.StartedAt,
                    LastSeenAt = found.LastSeenAt,
                    HeartbeatSeconds = found.HeartbeatSeconds,
                };
                await new Sora.Flow.Actions.ControlResponse<Sora.Flow.Model.AdapterAnnouncement>
                {
                    Model = "adapter",
                    Verb = Sora.Flow.Web.Infrastructure.WebConstants.Control.Verbs.Announce,
                    Status = Sora.Flow.Web.Infrastructure.WebConstants.Control.Status.Ok,
                    Message = null,
                    CorrelationId = env.CorrelationId,
                    Payload = payload
                }.Send(ct);
            }
            catch { /* best-effort */ }
    }));
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("routes[0]", "/admin/replay");
    report.AddSetting("routes[1]", "/admin/reproject");
    report.AddSetting("routes[2]", "/models/{model}/views/{view}/{referenceUlid}");
    report.AddSetting("routes[3]", "/models/{model}/views/{view}");
    report.AddSetting("routes[4]", "/policies");
    report.AddSetting("routes[5]", $"{Sora.Flow.Web.Infrastructure.WebConstants.Routes.DefaultPrefix}/{{model}}");
    report.AddSetting("routes[6]", "/api/vo/{type}");
    var autoReg = cfg.GetValue<bool?>("Sora:Flow:AutoRegister") ?? true;
    report.AddSetting("turnkey.autoRegister", autoReg.ToString().ToLowerInvariant());
    }

    private static IEnumerable<Type> DiscoverModels()
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
                if (bt.GetGenericTypeDefinition() != typeof(Sora.Flow.Model.FlowEntity<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }

    private static IEnumerable<Type> DiscoverValueObjects()
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
                if (bt.GetGenericTypeDefinition() != typeof(Sora.Flow.Model.FlowValueObject<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }
}
