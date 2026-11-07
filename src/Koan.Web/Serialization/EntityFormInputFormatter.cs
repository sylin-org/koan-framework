using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Endpoints;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Koan.Web.Serialization;

/// <summary>
/// Binds a page's form to the entity a declared <see cref="EntityController{TEntity,TKey}"/> write takes (WEB-0088), as the JSON formatter
/// binds the same members sent as JSON: through <see cref="SentForm"/>, with the serializer field access prepares for the reader. It reads
/// for nothing else, so every other action, and every undeclared controller, takes the bodies it always took.
/// </summary>
internal sealed class EntityFormInputFormatter : IInputFormatter
{
    public bool CanRead(InputFormatterContext context)
    {
        if (!context.HttpContext.Request.HasFormContentType) return false;
        if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not { } action) return false;
        return EntityOperation.IsEntityOperation(action.MethodInfo)
               && EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo).Declared
               && EntityOperation.EntityOf(action.ControllerTypeInfo) == context.ModelType;
    }

    public async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
        var http = context.HttpContext;
        var action = http.GetEndpoint()!.Metadata.GetMetadata<ControllerActionDescriptor>()!;
        var operation = EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo);
        var settings = http.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings;
        var effective = http.RequestServices.GetRequiredService<EntityRequestContextBuilder>().Build(new QueryOptions(), http.RequestAborted, http);
        var access = await FieldAccess.Prepare(context.ModelType, http.RequestServices, effective.User, http.RequestAborted).ConfigureAwait(false);
        var serializer = JsonSerializer.Create(access.RequiresGuardedSerialization ? access.CreateSerializerSettings(settings) : settings);

        var form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        var (words, unfit) = SentForm.Words(form, operation, serializer);
        if (unfit is not null)
        {
            context.ModelState.TryAddModelError(unfit.Field ?? context.ModelName, unfit.Text);
            return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
        }

        try
        {
            return await InputFormatterResult.SuccessAsync(words.ToObject(context.ModelType, serializer)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            context.ModelState.TryAddModelError(context.ModelName, error.Message);
            return await InputFormatterResult.FailureAsync().ConfigureAwait(false);
        }
    }
}
