using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;

namespace Koan.Web.Filters;

/// <summary>
/// A collection whose parameter has a default, as <c>string[]? Permissions = null</c> does. ASP.NET takes an empty bound collection there
/// for none, and gives the default; a JSON field sent as <c>[]</c> reaches binding as a name with no values (<see cref="ToolFilter"/>),
/// and binds empty, which is not a field left out. Sits before ASP.NET's collection binders, after what binds a body, bytes or a dictionary.
/// </summary>
internal sealed class SentEmptyCollectionBinderProvider : IModelBinderProvider
{
    private static readonly ArrayModelBinderProvider Arrays = new();
    private static readonly CollectionModelBinderProvider Collections = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => (Arrays.GetBinder(context) ?? Collections.GetBinder(context)) is { } collection ? new Binder(collection) : null;

    private sealed class Binder(IModelBinder collection) : IModelBinder
    {
        public async Task BindModelAsync(ModelBindingContext context)
        {
            await collection.BindModelAsync(context);
            if (context.Result.IsModelSet || !context.ValueProvider.ContainsPrefix(context.ModelName)) return;
            var (type, element) = (context.ModelType, context.ModelMetadata.ElementType!);
            context.Result = ModelBindingResult.Success(type.IsArray ? Array.CreateInstance(element, 0)
                : type.IsInterface ? Activator.CreateInstance(typeof(List<>).MakeGenericType(element))
                : Activator.CreateInstance(type));
        }
    }
}
