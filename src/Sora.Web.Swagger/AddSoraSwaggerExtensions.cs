using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Sora.Web.Swagger;

public static class AddSoraSwaggerExtensions
{
    public static IServiceCollection AddSoraSwagger(this IServiceCollection services, IConfiguration? config = null)
    {
        // Idempotency: if Swagger services are already registered, skip to avoid duplicate docs/config actions
        if (services.Any(d => d.ServiceType == typeof(ISwaggerProvider)))
        {
            return services;
        }
        if (config is null)
        {
            // Delay resolve until app builds; rely on DI at UseSoraSwagger time if needed.
            // For Add phase, prefer having config passed in; otherwise, we skip config-dependent parts.
        }
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Sora API", Version = "v1" });
            var opts = config is not null ? GetOptions(config) : new SoraWebSwaggerOptions();
            if (opts.IncludeXmlComments)
            {
                foreach (var xml in GetXmlDocFiles())
                {
                    try { c.IncludeXmlComments(xml, includeControllerXmlComments: true); } catch { }
                }
            }
            // Document common headers
            c.OperationFilter<SoraHeadersOperationFilter>();
            // If transformers assembly is present, include an operation filter to advertise alternate media types
            if (Type.GetType("Sora.Web.Transformers.EnableEntityTransformersAttribute, Sora.Web.Transformers") is not null)
            {
                c.OperationFilter<TransformerMediaTypesOperationFilter>();
            }
        });
        return services;
    }

    public static WebApplication UseSoraSwagger(this WebApplication app)
    {
        var env = app.Environment;
        var cfg = app.Configuration;
        var opts = GetOptions(cfg);

        bool enabled;
        if (opts.Enabled.HasValue)
        {
            enabled = opts.Enabled.Value;
        }
        else if (env.IsProduction())
        {
            enabled = cfg.GetValue<bool?>("Sora__Web__Swagger__Enabled") == true ||
                      cfg.GetValue<bool?>("Sora:AllowMagicInProduction") == true;
        }
        else
        {
            // Adding the module indicates intent to use it; enable by default outside Production
            enabled = true;
        }

        if (!enabled) return app; // off in non-dev unless explicitly enabled via env or magic flag

        // Ensure services were registered; if not, skip to avoid runtime 500s
    var provider = app.Services.GetService<ISwaggerProvider>();
        if (provider is null)
        {
            app.Logger.LogWarning("Sora.Web.Swagger: Swagger services not found. Did you call services.AddSoraSwagger()? Skipping UI middleware.");
            return app;
        }

        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            ui.RoutePrefix = opts.RoutePrefix;
            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Sora API v1");
        });

        // Optionally protect UI outside Development
        if (!env.IsDevelopment() && opts.RequireAuthOutsideDevelopment)
        {
            app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments($"/{opts.RoutePrefix}"), b =>
            {
                b.UseAuthentication();
                b.UseAuthorization();
            });
        }

        return app;
    }

    private static SoraWebSwaggerOptions GetOptions(IConfiguration cfg)
    {
        var o = new SoraWebSwaggerOptions();
        cfg.GetSection("Sora:Web:Swagger").Bind(o);
    // also support Sora__Web__Swagger__Enabled env var
        var envEnabled = cfg.GetValue<bool?>("Sora__Web__Swagger__Enabled");
        if (envEnabled.HasValue) o.Enabled = envEnabled;
    // magic flag unified across Sora
    var magic = cfg.GetValue<bool?>("Sora:AllowMagicInProduction");
    if (magic == true) o.Enabled = true;
        return o;
    }

    private static IEnumerable<string> GetXmlDocFiles()
    {
        var baseDir = AppContext.BaseDirectory;
        foreach (var file in Directory.EnumerateFiles(baseDir, "*.xml", SearchOption.TopDirectoryOnly))
            yield return file;
    }
}

// Lives in Swagger assembly and uses reflection to talk to Transformers without a direct reference
internal sealed class TransformerMediaTypesOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    private readonly IServiceProvider _services;
    public TransformerMediaTypesOperationFilter(IServiceProvider services) { _services = services; }
    public void Apply(Microsoft.OpenApi.Models.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var attrType = Type.GetType("Sora.Web.Transformers.EnableEntityTransformersAttribute, Sora.Web.Transformers");
        var registryType = Type.GetType("Sora.Web.Transformers.ITransformerRegistry, Sora.Web.Transformers");
        if (attrType is null || registryType is null) return;
        var hasAttr = context.MethodInfo.DeclaringType?.GetCustomAttributes(attrType, inherit: true).Any() == true;
        if (!hasAttr) return;

        var registry = _services.GetService(registryType);
        if (registry is null) return;

        Type? entityType = TryGetEntityType(context.MethodInfo.ReturnType);
        if (entityType is not null)
        {
            var getMi = registryType.GetMethod("GetContentTypes")!.MakeGenericMethod(entityType);
            var list = (IReadOnlyList<string>?)getMi.Invoke(registry, Array.Empty<object>());
            if (list is not null && list.Count > 0 && operation.Responses.TryGetValue("200", out var ok))
            {
                foreach (var ct in list)
                    if (!ok.Content.ContainsKey(ct)) ok.Content[ct] = new Microsoft.OpenApi.Models.OpenApiMediaType();
            }
        }

        foreach (var p in context.MethodInfo.GetParameters())
        {
            var et = TryGetEntityType(p.ParameterType);
            if (et is null) continue;
            var getMi = registryType.GetMethod("GetContentTypes")!.MakeGenericMethod(et);
            var list = (IReadOnlyList<string>?)getMi.Invoke(registry, Array.Empty<object>());
            if (list is null || list.Count == 0) continue;
            operation.RequestBody ??= new Microsoft.OpenApi.Models.OpenApiRequestBody { Required = true };
            foreach (var ct in list)
                if (!operation.RequestBody.Content.ContainsKey(ct))
                    operation.RequestBody.Content[ct] = new Microsoft.OpenApi.Models.OpenApiMediaType();
        }
    }

    private static Type? TryGetEntityType(Type t)
    {
        if (t == typeof(void) || t == typeof(string)) return null;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Microsoft.AspNetCore.Mvc.ActionResult<>))
            t = t.GetGenericArguments()[0];
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            t = t.GetGenericArguments()[0];
        // implements IEntity<>
        var ok = t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition().FullName == "Sora.Data.Abstractions.IEntity`1");
        return ok ? t : null;
    }
}
