using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Mcp.Verbs.Tests;

/// <summary>What a sign is cut as.</summary>
public enum SignShape
{
    Round,
    Square,
}

/// <summary>K57: a row a declared controller writes from JSON, a form and a tool, within the limits its routes declare.</summary>
public sealed class Sign : Entity<Sign>
{
    public string Text { get; set; } = "";

    public SignShape Shape { get; set; }
}

/// <summary>What putting up a sign takes beside it.</summary>
public sealed record Hanging([property: Description("The shape of the frame it hangs in.")] SignShape? Frame = null);

/// <summary>Puts up and changes signs, a change taking a body of at most <see cref="Small"/> bytes.</summary>
[Route("/signs")]
[Declared]
public sealed class SignsController : EntityController<Sign>
{
    public const int Small = 512;

    [Tool(Name = "put_up_sign")]
    [Description("Put up a sign.")]
    [Writes(nameof(Sign.Id), nameof(Sign.Text), nameof(Sign.Shape))]
    [Input<Hanging>]
    public override Task<IActionResult> Upsert(Sign model, CancellationToken ct) => base.Upsert(model, ct);

    [HttpPatch("{sign}")]
    [Tool(Name = "reshape_sign")]
    [Description("Change what a sign says or its shape.")]
    [Writes(nameof(Sign.Text), nameof(Sign.Shape))]
    [RequestSizeLimit(Small)]
    public override Task<IActionResult> Patch(string sign, CancellationToken ct) => base.Patch(sign, ct);
}

/// <summary>A form route no declared controller serves, beside one that does, holding its own limit.</summary>
[Route("/sign-letters")]
public sealed class SignLettersController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(SignsController.Small)]
    public async Task<IActionResult> Count(CancellationToken ct)
    {
        try
        {
            var form = await Request.ReadFormAsync(ct);
            return Ok(new { letters = form["text"].ToString().Length });
        }
        catch (BadHttpRequestException refused)
        {
            return StatusCode(refused.StatusCode);
        }
    }
}

/// <summary>Keeps the store until <paramref name="Until"/> completes, saying when it began through <paramref name="Running"/>; writes nothing.</summary>
public sealed record Holding(Task Until, TaskCompletionSource Running) : ICommand<bool>
{
    public async Task<Outcome<bool>> Run(CancellationToken ct)
    {
        Running.SetResult();
        await Until.WaitAsync(ct);
        return new Outcome<bool>.Done(true);
    }
}
