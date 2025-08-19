using HotChocolate;
using HotChocolate.Execution;
using System.Diagnostics;

namespace Sora.Web.GraphQl.Errors;

// Adds safe diagnostics to GraphQL errors without changing core semantics
public sealed class SoraGraphQlErrorFilter : IErrorFilter
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
