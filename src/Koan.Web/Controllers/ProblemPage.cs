using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Controllers;

/// <summary>A problem as a person reads it when the application supplies no page of its own: its heading, its sentence, what a field was refused for and the next step.</summary>
internal sealed class ProblemPage : IProblemView
{
    public static readonly ProblemPage Instance = new();

    public IActionResult Render(HttpContext http, ProblemDetails problem)
    {
        var page = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>")
            .Append(WebUtility.HtmlEncode(problem.Title)).Append("</title></head><body><h1>")
            .Append(WebUtility.HtmlEncode(problem.Title)).Append("</h1>");
        if (problem.Detail is { Length: > 0 } detail) page.Append("<p>").Append(WebUtility.HtmlEncode(detail)).Append("</p>");
        if (problem is ValidationProblemDetails { Errors.Count: > 0 } invalid)
        {
            page.Append("<ul>");
            foreach (var (field, errors) in invalid.Errors)
                foreach (var error in errors)
                    page.Append("<li>").Append(WebUtility.HtmlEncode(field.Length > 0 ? $"{field}: {error}" : error)).Append("</li>");
            page.Append("</ul>");
        }

        if (problem.Extensions.TryGetValue("next", out var next) && next is string address)
            page.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(address)).Append("\">Continue</a></p>");
        return new ContentResult
        {
            StatusCode = problem.Status,
            ContentType = "text/html; charset=utf-8",
            Content = page.Append("</body></html>").ToString(),
        };
    }
}
