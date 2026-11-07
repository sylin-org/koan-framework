using System.Globalization;
using System.Text.Json;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;

namespace Koan.Web.Filters;

/// <summary>What every <c>[Tool]</c> action answers to after its gate (<see cref="GateFilter"/>), on either door (ARCH-0141): a body read as a form
/// or one JSON object alike, and a value that cannot bind refused with its field named.</summary>
internal sealed class ToolFilter : IResourceFilter, IActionFilter, IExceptionFilter
{
    public static readonly ToolFilter Instance = new();

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.ActionDescriptor.Parameters.All(parameter => parameter.BindingInfo?.BindingSource != BindingSource.Body))
            context.ValueProviderFactories.Add(JsonFields.Instance);
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid) context.Result = Answers.Unbound(context.HttpContext, context.ModelState);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    /// <summary>A JSON body past the route's limit, met wherever binding read it, is refused as a tool call of that size is.</summary>
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }) return;
        context.Result = Answers.TooLarge();
        context.ExceptionHandled = true;
    }

    /// <summary>A JSON object's members as form values, after the route's so that a body never names the address.</summary>
    private sealed class JsonFields : IValueProviderFactory
    {
        public static readonly JsonFields Instance = new();

        public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
        {
            var request = context.ActionContext.HttpContext.Request;
            if (!request.HasJsonContentType()) return;
            request.EnableBuffering();
            try
            {
                // A wrapper that says JSON on every request, a GET's included, has sent no fields.
                if (await request.Body.ReadAsync(new byte[1], request.HttpContext.RequestAborted) == 0) return;
                request.Body.Position = 0;
                using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
                Flatten(json.RootElement, "", fields);
                context.ValueProviders.Add(new JQueryFormValueProvider(BindingSource.Form, fields, CultureInfo.InvariantCulture));
            }
            catch (JsonException)
            {
                context.ActionContext.ModelState.TryAddModelError("", "The body is not one JSON object.");
            }
            finally
            {
                request.Body.Position = 0;
            }
        }

        private static void Flatten(JsonElement element, string name, Dictionary<string, StringValues> fields)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                        Flatten(member.Value, name.Length == 0 ? member.Name : $"{name}.{member.Name}", fields);
                    break;
                case JsonValueKind.Array:
                    // The name with no values: a collection binds empty, which is not a field left out.
                    if (element.GetArrayLength() == 0) fields[name] = StringValues.Empty;
                    var index = 0;
                    foreach (var item in element.EnumerateArray()) Flatten(item, $"{name}[{index++}]", fields);
                    break;
                case JsonValueKind.String:
                    fields[name] = element.GetString();
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                default:
                    fields[name] = element.GetRawText();
                    break;
            }
        }
    }
}
