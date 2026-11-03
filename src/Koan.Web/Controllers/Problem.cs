using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Controllers;

/// <summary>The one document a refusal answers with, at every Koan door (ARCH-0143): its status, a title, a sentence as <c>detail</c>, its
/// <c>kind</c>, a gate's stable word as <c>gate</c>, and what each field a value could not bind to was refused for as <c>errors</c>. The kind
/// follows from the status, so no problem says one its status contradicts; the title is the kind's name unless an application names its own.
/// It answers <c>application/problem+json</c>. An application that renders pages claims that type for this record with its own formatter,
/// which it can because the record is sealed and plain, so the field-access guard leaves its formatter to the application.</summary>
public sealed record Problem(int Status, string? Detail = null, string? Gate = null, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    private const string MediaType = "application/problem+json";

    public string Kind => Status switch
    {
        StatusCodes.Status400BadRequest or StatusCodes.Status413PayloadTooLarge or StatusCodes.Status415UnsupportedMediaType or StatusCodes.Status422UnprocessableEntity => "invalid",
        StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden => "gated",
        StatusCodes.Status404NotFound => "absent",
        StatusCodes.Status409Conflict => "conflict",
        _ => "failed",
    };

    public string Title { get => field ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Kind); init; }

    public ObjectResult ToResult() => new(this) { StatusCode = Status, ContentTypes = { MediaType } };
}
