using System.Reflection;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Options;
using HotChocolate.Types;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Data.Abstractions;
using Sora.Data.Core;

namespace Sora.Web.GraphQl;

public static class AddSoraGraphQlExtensions
{
    private static bool _registered;

    public static IServiceCollection AddSoraGraphQl(this IServiceCollection services)
    {
        if (_registered) return services; // idempotent
        _registered = true;

        // Discover IEntity<> types to add object types dynamically
        var entityTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)))
            .ToArray();

        services
            .AddGraphQLServer()
            .SetRequestOptions(_ => new RequestExecutorOptions
            {
                ExecutionTimeout = TimeSpan.FromSeconds(10)
            })
            .AddQueryType(d => d.Name("Query"))
            .AddMutationType(d => d.Name("Mutation"));

        // Register generic resolvers for each entity as fields: items, item, upsertItem
        foreach (var t in entityTypes)
        {
            RegisterEntity(services, t);
        }

        return services;
    }

    private static void RegisterEntity(IServiceCollection services, Type entityType)
    {
        var name = entityType.Name; // e.g., Item
        var keyType = entityType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))
            .GetGenericArguments()[0];

        var resolverType = typeof(Resolvers<>).MakeGenericType(entityType);
        services.AddScoped(resolverType);

        services.AddGraphQLServer()
            .AddType(new ObjectType(descriptor =>
            {
                descriptor.Name(name);
                descriptor.Field("id").Type<StringType>().Resolve(ctx => ctx.Parent<object>()?.GetType().GetProperty("Id")?.GetValue(ctx.Parent<object>())?.ToString());
                descriptor.Field("name").Type<StringType>().Resolve(ctx => ctx.Parent<object>()?.GetType().GetProperty("Name")?.GetValue(ctx.Parent<object>())?.ToString());
                descriptor.Field("display").Type<StringType>().Resolve(ctx =>
                {
                    var o = ctx.Parent<object>();
                    var title = o?.GetType().GetProperty("Name")?.GetValue(o) as string
                             ?? o?.GetType().GetProperty("Title")?.GetValue(o) as string
                             ?? o?.GetType().GetProperty("Label")?.GetValue(o) as string
                             ?? o?.ToString();
                    return title ?? string.Empty;
                });
            }))
            .AddType(new ObjectType(descriptor =>
            {
                descriptor.Name(name + "Collection");
                descriptor.Field("items").Type(new ListType(new ObjectType(x => x.Name(name))));
                descriptor.Field("totalCount").Type<IntType>();
            }))
            .AddTypeExtension(new ObjectTypeExtension(descriptor =>
            {
                descriptor.Name("Query");
                // items(page,size)
                descriptor.Field(ToCamelPlural(name))
                    .Argument("page", a => a.Type<IntType>())
                    .Argument("size", a => a.Type<IntType>())
                    .Type(new ObjectType(d => d.Name(name + "Collection")))
                    .Resolve(ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        int page = ctx.ArgumentValue<int?>("page") ?? 1;
                        int size = ctx.ArgumentValue<int?>("size") ?? 50;
                        return res.GetItemsAsync(ctx, page, size);
                    });

                // item(id)
                descriptor.Field(ToCamelCase(name))
                    .Argument("id", a => a.Type<NonNullType<StringType>>())
                    .Type(new ObjectType(d => d.Name(name)))
                    .Resolve(ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        var id = ctx.ArgumentValue<string>("id");
                        return res.GetByIdAsync(ctx, id);
                    });
            }))
            .AddTypeExtension(new ObjectTypeExtension(descriptor =>
            {
                descriptor.Name("Mutation");
                var inputName = name + "Input";
                descriptor.Field("upsert" + name)
                    .Argument("input", a => a.Type(new InputObjectType(d => d.Name(inputName))))
                    .Type(new ObjectType(d => d.Name(name)))
                    .Resolve(ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        var input = ctx.ArgumentValue<object>("input");
                        return res.UpsertAsync(ctx, input);
                    });
            }))
            .AddType(new InputObjectType(d =>
            {
                d.Name(name + "Input");
                // Best-effort: map all public writable scalar properties except Id
                foreach (var p in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!p.CanWrite) continue;
                    if (string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.PropertyType.IsClass && p.PropertyType != typeof(string)) continue; // keep v1 simple
                    d.Field(p.Name).Type<StringType>(); // treat as string for v1 simplicity
                }
            }));
    }

    private static string ToCamelCase(string name) => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    private static string ToCamelPlural(string name)
        => ToCamelCase(name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name : name + "s");

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
    }

    public static IApplicationBuilder UseSoraGraphQl(this IApplicationBuilder app)
    {
        var cfg = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var path = cfg[Infrastructure.Constants.Configuration.Path] ?? "/graphql";
        // Controller-hosted route will be /graphql via MVC, but HotChocolate also uses middleware; keep only schema services.
        return app;
    }

    // Generic per-entity resolvers
    private sealed class Resolvers<TEntity>
        where TEntity : class, IEntity<string>
    {
        private readonly IServiceProvider _sp;
        public Resolvers(IServiceProvider sp) { _sp = sp; }

        public async Task<object> GetItemsAsync(IResolverContext ctx, int page, int size)
        {
            var repo = _sp.GetRequiredService<IDataService>().GetRepository<TEntity, string>();
            var items = await repo.QueryAsync(null, ctx.RequestAborted);
            var list = items.ToList();
            var total = list.Count;
            var skip = Math.Max(page, 1);
            var take = Math.Max(size, 1);
            var sliced = list.Skip((skip - 1) * take).Take(take).ToList();
            return new Dictionary<string, object?>
            {
                ["items"] = sliced,
                ["totalCount"] = total
            };
        }

        public Task<TEntity?> GetByIdAsync(IResolverContext ctx, string id)
            => Data<TEntity>.GetAsync(id, ctx.RequestAborted);

        public async Task<TEntity> UpsertAsync(IResolverContext ctx, object input)
        {
            // naive binder: set writable scalar props from input dict
            var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(input));
            var model = Activator.CreateInstance<TEntity>();
            foreach (var prop in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanWrite) continue;
                if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string)) continue;
                if (doc.RootElement.TryGetProperty(prop.Name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var str = v.GetString();
                    prop.SetValue(model, str);
                }
            }
            return await model.Upsert<TEntity, string>(ctx.RequestAborted);
        }
    }
}
