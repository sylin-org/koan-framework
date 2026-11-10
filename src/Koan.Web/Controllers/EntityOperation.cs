using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Endpoints;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Controllers;

/// <summary>
/// What one action of an <see cref="EntityController{TEntity,TKey}"/> serves and takes (WEB-0087), compiled once per action and read by
/// every door: whether its controller is <see cref="DeclaredAttribute">declared</see>, the members it writes (<see cref="WritesAttribute"/>),
/// the input it takes beside them (<see cref="InputAttribute{TInput}"/>), the order a declared collection continues through (DATA-0170), which
/// operation it serves (WEB-0089), and the partitions its controller serves
/// (<see cref="PartitionsAttribute"/>). REST admits a request by it and the route tool's schema is drawn from it, so the two cannot differ.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EntityOperation
{
    private static readonly ConcurrentDictionary<(MethodInfo? Action, Type Controller), EntityOperation> Compiled = new();

    private EntityOperation(MethodInfo? action, Type controller)
    {
        Declared = IsDeclared(controller);
        Partitions = controller.GetCustomAttribute<PartitionsAttribute>(inherit: true);
        Pagination = action?.GetCustomAttribute<PaginationAttribute>(inherit: true) ?? controller.GetCustomAttribute<PaginationAttribute>(inherit: true);
        Kind = action is not null && IsEntityOperation(action) ? KindOf(action.GetBaseDefinition().Name) : EntityEndpointOperationKind.None;
        HandsRefusedPage = controller.GetMethod(nameof(IRefusedPage.RefusedPage), BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(EntityEndpointOperationKind), typeof(Problem), typeof(CancellationToken)]) is { } page && !IsEntityController(page.DeclaringType);
        var entity = EntityOf(controller);
        if (action is null || entity is null) return;
        _members = entity.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(member => member.GetIndexParameters().Length == 0).ToArray();
        if (action.GetCustomAttribute<WritesAttribute>(inherit: true) is { } writes)
            Writes = writes.Members.Select(name => entity.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } member
                ? member
                : throw new InvalidOperationException($"[Writes] on {controller.Name}.{action.Name} names '{name}', which is not a writable member of {entity.Name}.")).ToArray();
        if (action.GetCustomAttributes(inherit: true).FirstOrDefault(attribute => attribute is IInputAttribute) is IInputAttribute input)
        {
            Input = input.Type;
            InputMembers = input.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(member => member.GetIndexParameters().Length == 0).ToArray();
            // An input is what an operation takes beside the entity, so a member of the same name would write the entity past its write set.
            if (InputMembers.FirstOrDefault(member => entity.GetProperty(member.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null) is { } shared)
                throw new InvalidOperationException($"The input {input.Type.Name} of {controller.Name}.{action.Name} names '{shared.Name}', which is a member of {entity.Name}. An input holds what is not the entity's; name it otherwise, or write the member.");
        }
        if (Declared && action.Name == nameof(EntityController<,>.GetCollection) && !IsInherited(action))
        {
            // DATA-0170: a declared collection continues from a sealed key in the order its [Pagination] names, and pages by nothing else.
            if (InputMembers.FirstOrDefault(member => PagingWords.Any(word => Named(member, word))) is { } paging)
                throw new InvalidOperationException($"The input of {controller.Name}.{action.Name} names '{paging.Name}', but a declared collection continues from 'after', a page of 'size' rows, and pages by nothing else. Name it otherwise.");
            try
            {
                Keyset = typeof(Koan.Data.Core.Querying.Keyset<>).MakeGenericType(entity)
                    .GetMethod(nameof(Koan.Data.Core.Querying.Keyset<object>.By), [typeof(string)])!.Invoke(null, [Pagination?.DefaultSort]);
            }
            catch (TargetInvocationException error) when (error.InnerException is { } inner)
            {
                throw new InvalidOperationException($"The order of {controller.Name}.{action.Name}, [Pagination(DefaultSort = \"{Pagination?.DefaultSort}\")], is not one a list can continue through: {inner.Message}", inner);
            }
        }
    }

    /// <summary>The endpoint operation an action of <see cref="EntityController{TEntity,TKey}"/> runs, the entry point it calls: each action
    /// its own, and a <c>PUT</c> the upsert it writes through.</summary>
    private static EntityEndpointOperationKind KindOf(string action) => action switch
    {
        nameof(EntityController<,>.GetCollection) => EntityEndpointOperationKind.Collection,
        nameof(EntityController<,>.Query) => EntityEndpointOperationKind.Query,
        nameof(EntityController<,>.GetNew) => EntityEndpointOperationKind.GetNew,
        nameof(EntityController<,>.GetById) => EntityEndpointOperationKind.GetById,
        nameof(EntityController<,>.Upsert) or nameof(EntityController<,>.Put) => EntityEndpointOperationKind.Upsert,
        nameof(EntityController<,>.UpsertMany) => EntityEndpointOperationKind.UpsertMany,
        nameof(EntityController<,>.Patch) => EntityEndpointOperationKind.Patch,
        nameof(EntityController<,>.Delete) => EntityEndpointOperationKind.Delete,
        nameof(EntityController<,>.DeleteMany) => EntityEndpointOperationKind.DeleteMany,
        nameof(EntityController<,>.DeleteByQuery) => EntityEndpointOperationKind.DeleteByQuery,
        nameof(EntityController<,>.DeleteAll) => EntityEndpointOperationKind.DeleteAll,
        _ => EntityEndpointOperationKind.None,
    };

    /// <summary>The words a declared collection pages by (DATA-0170), which an input of it cannot take.</summary>
    private static readonly string[] PagingWords = [Infrastructure.KoanWebConstants.Query.After, Infrastructure.KoanWebConstants.Query.Size, "page", "pageSize", "all"];

    /// <summary>Whether the controller serves only what it overrides (<see cref="DeclaredAttribute"/>).</summary>
    public bool Declared { get; }

    /// <summary>The endpoint operation this action runs (WEB-0089), as that endpoint tells its hooks;
    /// <see cref="EntityEndpointOperationKind.None"/> for an action that is no entity operation.</summary>
    internal EntityEndpointOperationKind Kind { get; }

    /// <summary>Whether the controller hands the page a refused form reads again (WEB-0089), by overriding <c>RefusedPage</c>.</summary>
    internal bool HandsRefusedPage { get; }

    /// <summary>The members this operation writes, or null where it writes the whole entity.</summary>
    public IReadOnlyList<PropertyInfo>? Writes { get; }

    /// <summary>The type of what this operation takes beside the entity's members, or null where it takes nothing else.</summary>
    public Type? Input { get; }

    /// <summary>The members of <see cref="Input"/>, each read from the query or the body under its wire name.</summary>
    public IReadOnlyList<PropertyInfo> InputMembers { get; } = [];

    /// <summary>The order a declared collection continues through (a <c>Keyset&lt;TEntity&gt;</c>, DATA-0170), or null where this operation is
    /// not one.</summary>
    public object? Keyset { get; }

    /// <summary>The <see cref="PaginationAttribute"/> this operation pages by, its action's or else its controller's, or null where neither names
    /// one. <see cref="PaginationPolicy.Of"/> bounds it by the host's safety bounds, so REST clamps a page and the route tool states its bound
    /// from the one policy.</summary>
    public PaginationAttribute? Pagination { get; }

    /// <summary>The partitions the controller serves to a caller who names one, or null where it serves none.</summary>
    public PartitionsAttribute? Partitions { get; }

    /// <summary>The compiled operation of <paramref name="action"/> as <paramref name="controller"/> answers it.</summary>
    public static EntityOperation Of(MethodInfo? action, Type controller)
        => Compiled.GetOrAdd((action, controller), static key => new EntityOperation(key.Action, key.Controller));

    /// <summary>Whether <paramref name="controller"/> is a declared <see cref="EntityController{TEntity,TKey}"/>.</summary>
    public static bool IsDeclared(Type controller) => controller.IsDefined(typeof(DeclaredAttribute), inherit: true);

    /// <summary>Whether <paramref name="action"/> is one of the operations <see cref="EntityController{TEntity,TKey}"/> defines, overridden or not.</summary>
    public static bool IsEntityOperation(MethodInfo action) => IsEntityController(action.GetBaseDefinition().DeclaringType);

    /// <summary>Whether <paramref name="action"/> is an operation its controller inherits without overriding it.</summary>
    public static bool IsInherited(MethodInfo action) => IsEntityController(action.DeclaringType);

    /// <summary>The name a member travels under on the wire.</summary>
    public static string WireName(PropertyInfo member)
        => member.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? char.ToLowerInvariant(member.Name[0]) + member.Name[1..];

    /// <summary>Whether a request may name <paramref name="name"/> in its query, or a body beside the entity: an input member, or the partition
    /// where the controller serves one.</summary>
    internal bool Takes(string name)
        => InputMembers.Any(member => Named(member, name))
           || Keyset is not null && (string.Equals(name, Infrastructure.KoanWebConstants.Query.After, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(name, Infrastructure.KoanWebConstants.Query.Size, StringComparison.OrdinalIgnoreCase))
           || Partitions is not null && string.Equals(name, Infrastructure.KoanWebConstants.Query.Set, StringComparison.OrdinalIgnoreCase);

    /// <summary>The member of the entity this operation writes under <paramref name="name"/>, or null.</summary>
    internal PropertyInfo? Written(string name) => Writes?.FirstOrDefault(member => Named(member, name));

    /// <summary>The members of the entity, which a body may name.</summary>
    private readonly PropertyInfo[] _members = [];

    /// <summary>The member a body names under <paramref name="name"/>: its input's, then the entity's; null where it names neither.</summary>
    internal PropertyInfo? Member(string name)
        => InputMembers.FirstOrDefault(member => Named(member, name)) ?? _members.FirstOrDefault(member => Named(member, name));

    /// <summary>What a body sent to this operation is refused for where it cannot bind (WEB-0087, WEB-0088): one that is not a JSON object, or
    /// the first value that cannot be the member of the entity or of the input it names, refused naming its field; null where it binds.</summary>
    internal Reason? Unfit(JToken? body, JsonSerializer serializer)
    {
        if (body is not JObject members) return NotOneObject;
        foreach (var member in members.Properties())
            if (Member(member.Name) is { } named && Unfit(member.Name, member.Value, named.PropertyType, serializer) is { } unfit) return unfit;
        return null;
    }

    /// <summary>What <paramref name="value"/>, sent as <paramref name="name"/>, is refused for where it cannot be <paramref name="type"/>; null where
    /// it can, or where it is no value. The value is read from its text, as a JSON body is read, so what the body's binding cannot take (1.5 for
    /// a whole number) is refused here too rather than converted.</summary>
    internal static Reason? Unfit(string name, JToken value, Type type, JsonSerializer serializer)
    {
        if (value.Type == JTokenType.Null) return null;
        try
        {
            using var reader = new JsonTextReader(new StringReader(value.ToString(Formatting.None)));
            serializer.Deserialize(reader, type);
            return null;
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidCastException or ArgumentException or OverflowException)
        {
            return CannotTake(name);
        }
    }

    /// <summary>What a value sent as <paramref name="name"/> that cannot be its member is refused with.</summary>
    internal static Reason CannotTake(string name) => Reason.Invalid(name, $"'{name}' cannot take this value.");

    /// <summary>What a write's body that is not one JSON object is refused with.</summary>
    internal static Reason NotOneObject => Reason.Invalid("A write is one JSON object of the members it writes.");

    internal static bool Named(PropertyInfo member, string name)
        => string.Equals(WireName(member), name, StringComparison.OrdinalIgnoreCase) || string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a request naming <paramref name="name"/>, which this operation does not take, is refused with.</summary>
    internal static Reason NotTaken(string name) => Reason.Invalid(name, $"This address takes no '{name}'.");

    private static bool IsEntityController(Type? type)
        => type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(EntityController<,>);

    /// <summary>The entity <paramref name="controller"/> serves, or null where it is no <see cref="EntityController{TEntity,TKey}"/>.</summary>
    internal static Type? EntityOf(Type controller)
    {
        for (var type = controller; type is not null; type = type.BaseType)
            if (IsEntityController(type)) return type.GetGenericArguments()[0];
        return null;
    }
}
