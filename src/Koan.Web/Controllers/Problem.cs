using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Controllers;

/// <summary>The one document a refusal answers with, at every Koan door (ARCH-0143, ARCH-0145): its status, a title, a sentence as
/// <c>detail</c>, its <c>kind</c>, the stable <c>word</c> a client acts on, which is the kind where nothing names another, and what each
/// field it concerns was refused for as <c>errors</c>. The kind follows from the status, so no problem says one its status contradicts; the
/// title is the kind's name unless an application names its own. It answers <c>application/problem+json</c>, and offers <c>text/html</c> to
/// negotiation: Koan writes no page, so an application that renders pages registers an ordinary <c>text/html</c> output formatter that writes
/// this record, and sets <c>MvcOptions.RespectBrowserAcceptHeader</c> so a browser's <c>Accept</c> counts. The record is sealed and plain, so
/// the field-access guard leaves that formatter to the application.</summary>
public sealed record Problem(int Status, string? Detail = null, string? Word = null, IReadOnlyDictionary<string, string[]>? Errors = null)
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

    /// <summary>The word a client branches on: the one the refusal names, and its kind where it names none.</summary>
    public string Word { get => field ?? Kind; init; } = Word;

    public string Title { get => field ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Kind); init; }

    public ObjectResult ToResult() => new(this) { StatusCode = Status, ContentTypes = { MediaType, "text/html" } };
}
