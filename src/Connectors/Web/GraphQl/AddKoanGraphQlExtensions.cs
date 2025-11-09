using Koan.Web.Endpoints;
using HotChocolate.Execution.Configuration;
using HotChocolate.Execution.Options;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Web.Filtering;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Connector.GraphQl;

public static class AddKoanGraphQlExtensions
{
    private static bool _registered;

    public static IServiceCollection AddKoanGraphQl(this IServiceCollection services)
    {
        if (_registered) return services;
        _registered = true;

        services.AddHttpContextAccessor();
        services.AddSingleton<Execution.IGraphQlExecutor, Execution.GraphQlExecutor>();

        // Bind typed options
        services.AddKoanOptions<GraphQlOptions>(Infrastructure.Constants.Configuration.Section);

        // Use cached assemblies instead of bespoke AppDomain scanning
        var entityTypes = AssemblyCache.Instance.GetAllAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(SafeGetTypes)
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)))
            // Avoid duplicate GraphQL names by keeping only the first type for each CLR simple name
            .GroupBy(t => t.Name)
            .Select(g => g.First())
            .ToArray();

        var builder = services
                .AddGraphQLServer()
                .SetRequestOptions(_ => new RequestExecutorOptions { ExecutionTimeout = TimeSpan.FromSeconds(10) })
                // Add a safe error filter that enriches errors with diagnostics in extensions
                .AddErrorFilter<Errors.KoanGraphQlErrorFilter>()
                .AddQueryType(d =>
                {
                    d.Name("Query");
                    // Always expose at least one field so the schema is valid
                    d.Field("entities")
                        .Description("Discovered IEntity<> types exposed in GraphQL (storage-based names)")
                        .Type<ListType<StringType>>()
                        .Resolve(ctx =>
                        {
                            var sp = ctx.Service<IServiceProvider>();
                            return entityTypes
                                .Select(t => ToGraphQlTypeName(ResolveStorageName(sp, t)))
                                .Distinct()
                                .OrderBy(n => n)
                                .ToArray();
                        });
                    d.Field("status")
                        .Description("Simple health probe")
                        .Type<StringType>()
                        .Resolve("ok");
                });

        // Only add Mutation root type if we have any entities to attach fields to
        if (entityTypes.Length > 0)
        {
            builder = builder.AddMutationType(d => d.Name("Mutation"));
        }

        foreach (var t in entityTypes)
        {
            RegisterEntity(builder, services, t);
        }

        return services;
    }

    private static void RegisterEntity(IRequestExecutorBuilder builder, IServiceCollection services, Type entityType)
    {
        // Compute GraphQL names from storage naming
        // Resolve storage name via a scoped provider at request time to avoid building a temporary container
        var nameRaw = ResolveStorageNameFactory(entityType);
        var name = ToGraphQlTypeName(nameRaw);
        var resolverType = typeof(Resolvers<>).MakeGenericType(entityType);
        services.AddScoped(resolverType);

        // Register entity GraphQL type with explicit field resolvers so no implicit binding is required
        builder.AddType(new ObjectType(descriptor =>
        {
            descriptor.Name(name);
            foreach (var p in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!p.CanRead) continue;
                var fieldName = ToCamelCase(p.Name);
                // Map simple primitives as strings for now; we primarily need id/name
                descriptor.Field(fieldName)
                    .Type<StringType>()
                    .Resolve(ctx =>
                    {
                        var parent = ctx.Parent<object?>();
                        if (parent is null) return null;
                        var val = p.GetValue(parent);
                        if (val is null) return null;
                        return val is string s ? s : val.ToString();
                    });
            }
        }));

        builder.AddTypeExtension(new ObjectTypeExtension(d =>
            {
                d.Name(name);
                d.Field("display").Type<StringType>().Resolve(ctx =>
                {
                    var o = ctx.Parent<object>();
                    var t = o?.GetType();
                    if (t is null) return string.Empty;
                    string? pick(params string[] names)
                        => names.Select(n => t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(o) as string)
                                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                    return pick("Display", "Name", "Title", "Label") ?? o!.ToString() ?? string.Empty;
                });
            }));

        // Concrete payload type per-entity to avoid generic name collisions
        var collectionPayloadName = $"{name}CollectionPayload";
        var payloadClr = typeof(CollectionPayload<>).MakeGenericType(entityType);
        var itemsProp = payloadClr.GetProperty("Items")!;
        var totalProp = payloadClr.GetProperty("TotalCount")!;
        builder.AddType(new ObjectType(od =>
        {
            od.Name(collectionPayloadName);
            od.Field("items")
                        .Type(new ListTypeNode(new NamedTypeNode(name)))
                        .Resolve(ctx =>
                        {
                            var parent = ctx.Parent<object?>();
                            return parent is null ? null : itemsProp.GetValue(parent);
                        });
            od.Field("totalCount")
                        .Type<IntType>()
                        .Resolve(ctx =>
                        {
                            var parent = ctx.Parent<object?>();
                            return parent is null ? 0 : (int)(totalProp.GetValue(parent) ?? 0);
                        });
        }));

        builder.AddTypeExtension(new ObjectTypeExtension(d =>
        {
            d.Name("Query");
            d.Field(ToGraphQlFieldNamePlural(nameRaw))
                .Argument("q", a => a.Type<StringType>())
                .Argument("filter", a => a.Type<StringType>())
                .Argument("filterObj", a => a.Type<AnyType>())
                .Argument("ignoreCase", a => a.Type<BooleanType>())
                .Argument("page", a => a.Type<IntType>())
                .Argument("size", a => a.Type<IntType>())
                .Type(new NamedTypeNode(collectionPayloadName))
                .Resolve(async ctx =>
                {
                    var res = (dynamic)ctx.Service(resolverType);
                    string? q = ctx.ArgumentValue<string?>("q");
                    string? fstr = ctx.ArgumentValue<string?>("filter");
                    object? fobj = ctx.ArgumentValue<object?>("filterObj");
                    string? filter = fstr ?? (fobj is null ? null : JsonConvert.SerializeObject(fobj));
                    bool ignore = ctx.ArgumentValue<bool?>("ignoreCase") ?? false;
                    int page = ctx.ArgumentValue<int?>("page") ?? 1;
                    int size = ctx.ArgumentValue<int?>("size") ?? KoanWebConstants.Defaults.DefaultPageSize;
                    return await res.GetItemsAsync(ctx, q, filter, ignore, page, size);
                });

            d.Field(ToGraphQlFieldName(nameRaw))
            .Argument("id", a => a.Type<NonNullType<StringType>>())
            .Type(new NamedTypeNode(name))
            .Resolve(async ctx =>
            {
                var res = (dynamic)ctx.Service(resolverType);
                var id = ctx.ArgumentValue<string>("id");
                return await res.GetByIdAsync(ctx, id);
            });
        }));

        // CLR-name-based alias fields for compatibility (e.g., item/items)
        var altTypeName = ToGraphQlTypeName(entityType.Name);
        var altPlural = ToCamelPlural(altTypeName);
        var altSingular = ToCamelCase(altTypeName);
        var storagePlural = ToGraphQlFieldNamePlural(nameRaw);
        var storageSingular = ToGraphQlFieldName(nameRaw);
        if (!string.Equals(altPlural, storagePlural, StringComparison.Ordinal))
        {
            builder.AddTypeExtension(new ObjectTypeExtension(d =>
            {
                d.Name("Query");
                d.Field(altPlural)
                    .Argument("q", a => a.Type<StringType>())
                    .Argument("filter", a => a.Type<StringType>())
                    .Argument("filterObj", a => a.Type<AnyType>())
                    .Argument("ignoreCase", a => a.Type<BooleanType>())
                    .Argument("page", a => a.Type<IntType>())
                    .Argument("size", a => a.Type<IntType>())
                    .Type(new NamedTypeNode(collectionPayloadName))
                    .Resolve(async ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        string? q = ctx.ArgumentValue<string?>("q");
                        string? fstr = ctx.ArgumentValue<string?>("filter");
                        object? fobj = ctx.ArgumentValue<object?>("filterObj");
                        string? filter = fstr ?? (fobj is null ? null : JsonConvert.SerializeObject(fobj));
                        bool ignore = ctx.ArgumentValue<bool?>("ignoreCase") ?? false;
                        int page = ctx.ArgumentValue<int?>("page") ?? 1;
                        int size = ctx.ArgumentValue<int?>("size") ?? KoanWebConstants.Defaults.DefaultPageSize;
                        return await res.GetItemsAsync(ctx, q, filter, ignore, page, size);
                    });
            }));
        }
        if (!string.Equals(altSingular, storageSingular, StringComparison.Ordinal))
        {
            builder.AddTypeExtension(new ObjectTypeExtension(d =>
            {
                d.Name("Query");
                d.Field(altSingular)
                    .Argument("id", a => a.Type<NonNullType<StringType>>())
                    .Type(new NamedTypeNode(name))
                    .Resolve(async ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        var id = ctx.ArgumentValue<string>("id");
                        return await res.GetByIdAsync(ctx, id);
                    });
            }));
        }

        var inputName = name + "Input";
        builder.AddType(new InputObjectType(d =>
            {
                d.Name(inputName);
                foreach (var p in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!p.CanWrite) continue;
                    if (string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (p.PropertyType.IsClass && p.PropertyType != typeof(string)) continue;
                    d.Field(ToCamelCase(p.Name)).Type<StringType>();
                }
            }));

        // Alias input type for CLR name (e.g., ItemInput)
        var altInputName = altTypeName + "Input";
        if (!string.Equals(altInputName, inputName, StringComparison.Ordinal))
        {
            builder.AddType(new InputObjectType(d =>
                {
                    d.Name(altInputName);
                    foreach (var p in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                    {
                        if (!p.CanWrite) continue;
                        if (string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                        if (p.PropertyType.IsClass && p.PropertyType != typeof(string)) continue;
                        d.Field(ToCamelCase(p.Name)).Type<StringType>();
                    }
                }));
        }

        builder.AddTypeExtension(new ObjectTypeExtension(d =>
        {
            d.Name("Mutation");
            d.Field("upsert" + name)
                .Argument("input", a => a.Type(new NamedTypeNode(inputName)))
                .Type(new NamedTypeNode(name))
                .Resolve(async ctx =>
                {
                    var res = (dynamic)ctx.Service(resolverType);
                    var input = ctx.ArgumentValue<object>("input");
                    return await res.UpsertAsync(ctx, input);
                });
        }));

        // Alias mutation for CLR name (e.g., upsertItem) using alt input
        if (!string.Equals(altTypeName, name, StringComparison.Ordinal))
        {
            builder.AddTypeExtension(new ObjectTypeExtension(d =>
            {
                d.Name("Mutation");
                d.Field("upsert" + altTypeName)
                    .Argument("input", a => a.Type(new NamedTypeNode(altInputName)))
                    .Type(new NamedTypeNode(name))
                    .Resolve(async ctx =>
                    {
                        var res = (dynamic)ctx.Service(resolverType);
                        var input = ctx.ArgumentValue<object>("input");
                        return await res.UpsertAsync(ctx, input);
                    });
            }));
        }
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private static string ToCamelPlural(string name)
        => ToCamelCase(name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name : name + "s");

    private static string ResolveStorageName(IServiceProvider sp, Type entityType)
    {
        // Resolve storage name without touching AggregateConfigs to avoid caching with a partial provider
        // 1) Determine provider: attribute wins; otherwise pick highest-priority IDataAdapterFactory
        var provider = entityType.GetCustomAttribute<DataAdapterAttribute>()?.Provider;
        if (string.IsNullOrWhiteSpace(provider))
        {
            var factories = sp.GetServices<IDataAdapterFactory>().ToList();
            if (factories.Count == 0)
            {
                provider = "json";
            }
            else
            {
                var chosen = factories
                    .Select(f => new
                    {
                        Factory = f,
                        Priority = (f.GetType().GetCustomAttributes(typeof(ProviderPriorityAttribute), inherit: false)
                            .FirstOrDefault() as ProviderPriorityAttribute)?.Priority ?? 0,
                        Name = f.GetType().Name
                    })
                    .OrderByDescending(x => x.Priority)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .First().Name;
                const string suffix = "AdapterFactory";
                if (chosen.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) chosen = chosen[..^suffix.Length];
                provider = chosen.ToLowerInvariant();
            }
        }

        // 2) DATA-0086: Resolve naming provider for this adapter
        // Factories are registered as IDataAdapterFactory/IVectorAdapterFactory, not INamingProvider
        // Use reflection to avoid hard dependency on Koan.Data.Vector.Abstractions
        var dataFactories = sp.GetServices<IDataAdapterFactory>().Cast<INamingProvider>();

        IEnumerable<INamingProvider> vectorFactories = Enumerable.Empty<INamingProvider>();
        try
        {
            var vectorAdapterFactoryType = Type.GetType("Koan.Data.Vector.Abstractions.IVectorAdapterFactory, Koan.Data.Vector.Abstractions");
            if (vectorAdapterFactoryType != null)
            {
                var getServicesMethod = typeof(ServiceProviderServiceExtensions)
                    .GetMethod(nameof(ServiceProviderServiceExtensions.GetServices))!
                    .MakeGenericMethod(vectorAdapterFactoryType);
                var services = (System.Collections.IEnumerable)getServicesMethod.Invoke(null, new object[] { sp })!;
                vectorFactories = services.Cast<INamingProvider>();
            }
        }
        catch
        {
            // Vector abstractions not available, continue with data factories only
        }

        var allFactories = dataFactories.Concat(vectorFactories);
        var namingProvider = allFactories.FirstOrDefault(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase));

        string baseName;
        if (namingProvider != null)
        {
            // Use adapter's naming provider
            baseName = namingProvider.GetStorageName(entityType, sp);
        }
        else
        {
            // Fallback to default naming conventions
            var diResolver = sp.GetService<IStorageNameResolver>() ?? new DefaultStorageNameResolver();
            var fallback = sp.GetService<Microsoft.Extensions.Options.IOptions<Koan.Data.Core.Naming.NamingFallbackOptions>>()?.Value;
            var convention = fallback is not null
                ? new StorageNameResolver.Convention(fallback.Style, fallback.Separator, fallback.Casing)
                : new StorageNameResolver.Convention(StorageNamingStyle.EntityType, ".", NameCasing.AsIs);
            baseName = StorageNameResolver.Resolve(entityType, convention);
        }

        // Ignore partition suffix for type/field names
        return baseName.Split('#')[0].Split(namingProvider?.RepositorySeparator ?? "#")[0];
    }

    private static string ResolveStorageNameFactory(Type entityType)
    {
        // Best-effort: use ambient AppHost.Current if available; otherwise fallback to defaults
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is not null) return ResolveStorageName(sp, entityType);
        // Fallback path without DI context
        return entityType.Name;
    }

    private static string ToGraphQlTypeName(string storageName)
    {
        // e.g., foo.bar -> FooBar; orders -> Orders (keep plural as-is)
        var parts = storageName
            .Replace('-', '_')
            .Split(new[] { '.', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : string.Empty));
        var name = string.Concat(parts);
        if (name.Length > 0 && char.IsDigit(name[0])) name = "_" + name;
        return name;
    }

    private static string ToGraphQlFieldName(string storageName)
    {
        var typeName = ToGraphQlTypeName(storageName);
        return ToCamelCase(typeName);
    }

    private static string ToGraphQlFieldNamePlural(string storageName)
    {
        var baseField = ToGraphQlFieldName(storageName);
        return baseField.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? baseField : baseField + "s";
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
    }

    public static IApplicationBuilder UseKoanGraphQl(this IApplicationBuilder app)
    {
        var _ = app.ApplicationServices.GetRequiredService<IConfiguration>();
        return app;
    }

    // Adds safe diagnostics to GraphQL errors without changing core semantics
    public sealed class KoanGraphQlErrorFilter : IErrorFilter
    {
        public IError OnError(IError error)
        {
            try
            {
                var builder = ErrorBuilder.FromError(error);

                // correlation/activity id if present
                var activityId = Activity.Current?.Id;
                if (!string.IsNullOrEmpty(activityId)) builder.SetExtension("correlationId", activityId);

                // field path
                var path = error.Path?.ToString();
                if (!string.IsNullOrEmpty(path)) builder.SetExtension("fieldPath", path);

                // bubble up exception details (type + message) for diagnostics when available
                // keep it lightweight; do not include stack traces
                var ex = error.Exception;
                if (ex is not null)
                {
                    builder.SetExtension("exception", ex.GetType().FullName ?? ex.GetType().Name);
                    if (!string.IsNullOrWhiteSpace(ex.Message))
                        builder.SetExtension("exceptionMessage", ex.Message);
                }

                // lightweight hint for common scalar coercion issues
                if (error.Message.Contains("cannot deserialize", StringComparison.OrdinalIgnoreCase) ||
                    error.Message.Contains("cannot represent", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SetExtension("hint", "A field declared as String likely returned a non-string value. The server includes a best-effort conversion, but older binaries may need a restart/redeploy.");
                }

                return builder.Build();
            }
            catch
            {
                return error;
            }
        }
    }

    private sealed class CollectionPayload<TEntity>
        where TEntity : class
    {
        public IReadOnlyList<TEntity> Items { get; init; } = Array.Empty<TEntity>();
        public long TotalCount { get; init; }
    }

    private sealed class Resolvers<TEntity>
        where TEntity : class, IEntity<string>
    {
        private readonly IServiceProvider _sp;
        public Resolvers(IServiceProvider sp) { _sp = sp; }

        private static IQueryCapabilities Caps(IDataRepository<TEntity, string> repo)
            => repo as IQueryCapabilities ?? new RepoCaps(QueryCapabilities.None);
        private sealed record RepoCaps(QueryCapabilities Cap) : IQueryCapabilities { public QueryCapabilities Capabilities => Cap; }

        private GraphQlHooksRunner<TEntity> GetRunner(HttpContext http) => new(http.RequestServices);

        private static QueryOptions BuildOptions(HttpContext http, string? q, int page, int size)
        {
            var opts = new QueryOptions
            {
                Q = q,
                Page = page > 0 ? page : 1,
                PageSize = size > 0 ? Math.Min(size, KoanWebConstants.Defaults.MaxPageSize) : KoanWebConstants.Defaults.DefaultPageSize
            };
            return opts;
        }

        public async Task<object> GetItemsAsync(IResolverContext ctx, string? q, string? filterJson, bool ignoreCase, int page, int size)
        {
            var http = ctx.Service<IHttpContextAccessor>().HttpContext;
            if (http is null) throw new InvalidOperationException("HttpContext not available");
            try
            {
                var repo = _sp.GetRequiredService<IDataService>().GetRepository<TEntity, string>();
                var caps = Caps(repo);
                var opts = BuildOptions(http, q, page, size);
                var requestCtx = new EntityRequestContext(http.RequestServices, opts, ctx.RequestAborted, http) { Capabilities = caps };
                var hctx = new HookContext<TEntity>(requestCtx);
                var runner = GetRunner(http);

                var auth = await runner.AuthorizeAsync(hctx, new AuthorizeRequest { Method = "POST", Action = ActionType.Read, Scope = ActionScope.Collection });
                if (auth is AuthorizeDecision.Forbid fbd) throw new GraphQLException(ErrorBuilder.New().SetMessage(fbd.Reason ?? "Forbidden").SetCode("FORBIDDEN").Build());
                if (auth is AuthorizeDecision.Challenge) throw new GraphQLException(ErrorBuilder.New().SetMessage("Unauthorized").SetCode("UNAUTHORIZED").Build());

                if (!await runner.BuildOptionsAsync(hctx, opts) || !await runner.BeforeCollectionAsync(hctx, opts))
                {
                    return new CollectionPayload<TEntity> { Items = Array.Empty<TEntity>(), TotalCount = 0 };
                }

                IReadOnlyList<TEntity> items;
                long total = 0;

                using (var _set = EntityContext.Partition(null!))
                {
                    if (!string.IsNullOrWhiteSpace(filterJson) && repo is ILinqQueryRepository<TEntity, string> lrepo)
                    {
                        if (!JsonFilterBuilder.TryBuild<TEntity>(filterJson!, out var pr, out var error, new JsonFilterBuilder.BuildOptions { IgnoreCase = ignoreCase }))
                            throw new GraphQLException(ErrorBuilder.New().SetMessage(error ?? "Invalid filter").SetCode("BAD_FILTER").Build());
                        items = await lrepo.QueryAsync(pr!, ctx.RequestAborted);
                        try
                        {
                            var countRequest = new CountRequest<TEntity> { Predicate = pr };
                            var countResult = await repo.CountAsync(countRequest, ctx.RequestAborted);
                            total = countResult.Value;
                        }
                        catch { total = items.Count; }
                    }
                    else if (!string.IsNullOrWhiteSpace(opts.Q) && repo is IStringQueryRepository<TEntity, string> srepo)
                    {
                        items = await srepo.QueryAsync(opts.Q!, ctx.RequestAborted);
                        try
                        {
                            var countRequest = new CountRequest<TEntity> { RawQuery = opts.Q };
                            var countResult = await repo.CountAsync(countRequest, ctx.RequestAborted);
                            total = countResult.Value;
                        }
                        catch { total = items.Count; }
                    }
                    else
                    {
                        items = await repo.QueryAsync(null, ctx.RequestAborted);
                        try
                        {
                            var countRequest = new CountRequest<TEntity>();
                            var countResult = await repo.CountAsync(countRequest, ctx.RequestAborted);
                            total = countResult.Value;
                        }
                        catch { total = items.Count; }
                    }
                }

                var list = items.ToList();
                var skip = (opts.Page - 1) * opts.PageSize;
                list = list.Skip(skip).Take(opts.PageSize).ToList();

                if (!await runner.AfterCollectionAsync(hctx, list))
                {
                    return new CollectionPayload<TEntity> { Items = Array.Empty<TEntity>(), TotalCount = 0 };
                }

                var payload = new CollectionPayload<TEntity> { Items = list, TotalCount = total };
                var emit = await runner.EmitCollectionAsync(hctx, payload);
                return emit.replaced ? emit.payload : payload;
            }
            catch (Exception ex)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(ex.Message)
                        .SetCode("INTERNAL")
                        .SetExtension("exception", ex.GetType().FullName)
                        .Build());
            }
        }

        public async Task<TEntity?> GetByIdAsync(IResolverContext ctx, string id)
        {
            var http = ctx.Service<IHttpContextAccessor>().HttpContext;
            if (http is null) throw new InvalidOperationException("HttpContext not available");
            try
            {
                var repo = _sp.GetRequiredService<IDataService>().GetRepository<TEntity, string>();
                var caps = Caps(repo);
                var opts = BuildOptions(http, null, 1, KoanWebConstants.Defaults.DefaultPageSize);
                var requestCtx = new EntityRequestContext(http.RequestServices, opts, ctx.RequestAborted, http) { Capabilities = caps };
                var hctx = new HookContext<TEntity>(requestCtx);
                var runner = GetRunner(http);

                var auth = await runner.AuthorizeAsync(hctx, new AuthorizeRequest { Method = "POST", Action = ActionType.Read, Scope = ActionScope.Model, Id = id });
                if (auth is AuthorizeDecision.Forbid fbd) throw new GraphQLException(ErrorBuilder.New().SetMessage(fbd.Reason ?? "Forbidden").SetCode("FORBIDDEN").Build());
                if (auth is AuthorizeDecision.Challenge) throw new GraphQLException(ErrorBuilder.New().SetMessage("Unauthorized").SetCode("UNAUTHORIZED").Build());

                if (!await runner.BeforeModelFetchAsync(hctx, id)) return default;
                var model = await Data<TEntity, string>.GetAsync(id, ctx.RequestAborted);
                await runner.AfterModelFetchAsync(hctx, model);
                if (model is null) return default;

                var emit = await runner.EmitModelAsync(hctx, model);
                return emit.replaced ? (TEntity)emit.payload : model;
            }
            catch (Exception ex)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(ex.Message)
                        .SetCode("INTERNAL")
                        .SetExtension("exception", ex.GetType().FullName)
                        .Build());
            }
        }

        public async Task<TEntity> UpsertAsync(IResolverContext ctx, object input)
        {
            var http = ctx.Service<IHttpContextAccessor>().HttpContext;
            if (http is null) throw new InvalidOperationException("HttpContext not available");
            try
            {
                var repo = _sp.GetRequiredService<IDataService>().GetRepository<TEntity, string>();
                var caps = Caps(repo);
                var opts = BuildOptions(http, null, 1, KoanWebConstants.Defaults.DefaultPageSize);
                var requestCtx = new EntityRequestContext(http.RequestServices, opts, ctx.RequestAborted, http) { Capabilities = caps };
                var hctx = new HookContext<TEntity>(requestCtx);
                var runner = GetRunner(http);

                var jobj = JObject.Parse(JsonConvert.SerializeObject(input));
                var model = Activator.CreateInstance<TEntity>()!;
                foreach (var prop in typeof(TEntity).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (!prop.CanWrite) continue;
                    if (string.Equals(prop.Name, "Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string)) continue;
                    if (jobj.TryGetValue(prop.Name, StringComparison.OrdinalIgnoreCase, out var v) && v.Type == JTokenType.String)
                    {
                        prop.SetValue(model, v.Value<string>());
                    }
                }

                var auth = await runner.AuthorizeAsync(hctx, new AuthorizeRequest { Method = "POST", Action = ActionType.Write, Scope = ActionScope.Model });
                if (auth is AuthorizeDecision.Forbid fbd) throw new GraphQLException(ErrorBuilder.New().SetMessage(fbd.Reason ?? "Forbidden").SetCode("FORBIDDEN").Build());
                if (auth is AuthorizeDecision.Challenge) throw new GraphQLException(ErrorBuilder.New().SetMessage("Unauthorized").SetCode("UNAUTHORIZED").Build());

                await runner.BeforeSaveAsync(hctx, model);
                var saved = await model.Upsert<TEntity, string>(ctx.RequestAborted);
                await runner.AfterSaveAsync(hctx, saved);

                var emit = await runner.EmitModelAsync(hctx, saved);
                return emit.replaced ? (TEntity)emit.payload : saved;
            }
            catch (Exception ex)
            {
                throw new GraphQLException(
                    ErrorBuilder.New()
                        .SetMessage(ex.Message)
                        .SetCode("INTERNAL")
                        .SetExtension("exception", ex.GetType().FullName)
                        .Build());
            }
        }
    }

    private sealed class GraphQlHooksRunner<TEntity>
    {
        private readonly IEnumerable<IAuthorizeHook<TEntity>> _auth;
        private readonly IEnumerable<IRequestOptionsHook<TEntity>> _opts;
        private readonly IEnumerable<ICollectionHook<TEntity>> _col;
        private readonly IEnumerable<IModelHook<TEntity>> _model;
        private readonly IEnumerable<IEmitHook<TEntity>> _emit;

        public GraphQlHooksRunner(IServiceProvider sp)
        {
            _auth = sp.GetServices<IAuthorizeHook<TEntity>>().OrderBy(i => i.Order).ToArray();
            _opts = sp.GetServices<IRequestOptionsHook<TEntity>>().OrderBy(i => i.Order).ToArray();
            _col = sp.GetServices<ICollectionHook<TEntity>>().OrderBy(i => i.Order).ToArray();
            _model = sp.GetServices<IModelHook<TEntity>>().OrderBy(i => i.Order).ToArray();
            _emit = sp.GetServices<IEmitHook<TEntity>>().OrderBy(i => i.Order).ToArray();
        }

        public async Task<AuthorizeDecision> AuthorizeAsync(HookContext<TEntity> ctx, AuthorizeRequest req)
        {
            foreach (var h in _auth)
            {
                var d = await h.OnAuthorizeAsync(ctx, req);
                if (d is AuthorizeDecision.Forbid or AuthorizeDecision.Challenge) return d;
            }
            return AuthorizeDecision.Allowed();
        }

        public async Task<bool> BuildOptionsAsync(HookContext<TEntity> ctx, QueryOptions opts)
        {
            foreach (var h in _opts)
            {
                await h.OnBuildingOptionsAsync(ctx, opts);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> BeforeCollectionAsync(HookContext<TEntity> ctx, QueryOptions opts)
        {
            foreach (var h in _col)
            {
                await h.OnBeforeFetchAsync(ctx, opts);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> AfterCollectionAsync(HookContext<TEntity> ctx, List<TEntity> items)
        {
            foreach (var h in _col)
            {
                await h.OnAfterFetchAsync(ctx, items);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> BeforeModelFetchAsync(HookContext<TEntity> ctx, string id)
        {
            foreach (var h in _model)
            {
                await h.OnBeforeFetchAsync(ctx, id);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> AfterModelFetchAsync(HookContext<TEntity> ctx, TEntity? model)
        {
            foreach (var h in _model)
            {
                await h.OnAfterFetchAsync(ctx, model);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> BeforeSaveAsync(HookContext<TEntity> ctx, TEntity model)
        {
            foreach (var h in _model)
            {
                await h.OnBeforeSaveAsync(ctx, model);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<bool> AfterSaveAsync(HookContext<TEntity> ctx, TEntity model)
        {
            foreach (var h in _model)
            {
                await h.OnAfterSaveAsync(ctx, model);
                if (ctx.IsShortCircuited) return false;
            }
            return true;
        }

        public async Task<(bool replaced, object payload)> EmitCollectionAsync(HookContext<TEntity> ctx, object payload)
        {
            foreach (var h in _emit)
            {
                var d = await h.OnEmitCollectionAsync(ctx, payload);
                if (d is EmitDecision.Replace rep) return (true, rep.Payload);
                if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitResult!);
            }
            return (false, payload);
        }

        public async Task<(bool replaced, object payload)> EmitModelAsync(HookContext<TEntity> ctx, object payload)
        {
            foreach (var h in _emit)
            {
                var d = await h.OnEmitModelAsync(ctx, payload);
                if (d is EmitDecision.Replace rep) return (true, rep.Payload);
                if (ctx.IsShortCircuited) return (true, ctx.ShortCircuitResult!);
            }
            return (false, payload);
        }
    }
}



