using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Mcp.Verbs.Tests;

public sealed class Place : Entity<Place>
{
    public bool Private { get; set; }
    public string[] Members { get; set; } = [];
}

public sealed class Topic : Entity<Topic>
{
    public string PlaceId { get; set; } = "";
    public bool Locked { get; set; }
}

public sealed class Post : Entity<Post>
{
    public string TopicId { get; set; } = "";
    public string? ParentId { get; set; }
    public string AuthorId { get; set; } = "";
    public string Body { get; set; } = "";
    public bool Notify { get; set; }
}

public sealed class Person : Entity<Person>
{
    public bool Frozen { get; set; }
}

public sealed class LogEntry : Entity<LogEntry>
{
    public static readonly Sequence Log = Sequence.Of<LogEntry>(entry => entry.Seq);

    [Index(Unique = true)]
    public long Seq { get; set; }

    public string What { get; set; } = "";
    public string Actor { get; set; } = "";
}

/// <summary>A reply: a principal as its author, a target from the route, an id minted when the caller names none, a body, a gate that
/// depends on the Topic addressed, and an entry in the same commit.</summary>
public sealed record Reply(string Id, string PlaceId, string TopicId, string? Body, string? AuthorId, string? ParentId, bool Notify) : ICommand<Post>
{
    public static readonly Reason NoTopic = Reason.Absent("No such Topic.");
    public static readonly Reason NoPlace = Reason.Absent("No such place, and a different sentence.");
    public static readonly Reason SignIn = Reason.Gated("sign-in", "Sign in to write here.");
    public static readonly Reason Locked = Reason.Gated("locked", "This Topic is locked, so no one replies in it.");
    public static readonly Reason Taken = Reason.Conflict("That id already holds other words.");
    public static readonly Reason NeedsBody = Reason.Invalid("A reply says something.");

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Place.Get(PlaceId, ct) is not { } place) return new Outcome<Post>.Refused(NoPlace);
        // A visitor to a private place is told what to do at every address in it, whether anything is there or not; someone signed in who may not see it is told nothing.
        if (place.Private && AuthorId is null) return new Outcome<Post>.Refused(SignIn);
        if (place.Private && !place.Members.Contains(AuthorId)) return new Outcome<Post>.Refused(NoTopic);
        if (await Topic.Get(TopicId, ct) is not { } topic || topic.PlaceId != place.Id) return new Outcome<Post>.Refused(NoTopic);
        if (AuthorId is null) return new Outcome<Post>.Refused(SignIn);
        if (topic.Locked) return new Outcome<Post>.Refused(Locked);
        if (string.IsNullOrWhiteSpace(Body)) throw new Refusal(NeedsBody);
        if (await Post.Get(Id, ct) is { } existing)
            return existing.Body == Body && existing.AuthorId == AuthorId && existing.TopicId == TopicId
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Refused(Taken);

        var post = await new Post { Id = Id, TopicId = TopicId, ParentId = ParentId, AuthorId = AuthorId, Body = Body, Notify = Notify }.Save(ct);
        await new LogEntry { Seq = await LogEntry.Log.Next(ct), What = $"reply:{post.Id}", Actor = AuthorId }.Save(ct);
        return new Outcome<Post>.Done(post);
    }
}

/// <summary>A caller supplies these; author and target never come from here.</summary>
public sealed record ReplyInput(
    [Description("The reply, in plain words.")] string? Body,
    [Description("The Post it answers; empty, it answers the Topic.")] string? Parent = null,
    [Description("Whether its parent's author is told.")] bool Notify = true,
    [Description("A chosen id; left out, the same words from the same author are one reply.")] string? Id = null);

public sealed class RepliesController : ControllerBase
{
    [HttpPost("/places/{place}/topics/{topic}/replies")]
    [Tool(Name = "reply")]
    [Description("Reply in a Topic.")]
    public async Task<IActionResult> Write(string place, string topic, ReplyInput input, CancellationToken ct)
    {
        var author = Forum.PersonId(User);
        var id = input.Id ?? Forum.Minted(author, topic, input.Parent, input.Body);
        return this.Answer(await new Reply(id, place, topic, input.Body, author, input.Parent, input.Notify).Send(ct),
            post => Created($"/places/{place}/topics/{topic}/posts/{post.Id}", post));
    }
}

/// <summary>A role change: an actor from the principal, a target from the route, a role from input, and a rule spanning all three.
/// The change is made before the rest of the rule is judged, so that a refusal or a failure has something to leave behind.</summary>
public sealed record ChangeRole(string ActorId, string PersonId, string Role, bool Give) : ICommand<string>
{
    public static readonly Reason NoOne = Reason.Absent("No such person.");
    public static readonly Reason NotYours = Reason.Gated("Only a steward changes a role, and never their own.");
    public static readonly Reason Frozen = Reason.Conflict("This person is frozen.");
    public static readonly Reason NotARole = Reason.Invalid("That role is not one given here.");

    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        if (await Person.Get(PersonId, ct) is not { } person) return new Outcome<string>.Refused(NoOne);
        var roles = AppHost.GetRequiredService<RoleCollection>("change a role");
        if (await roles.Get(Role, ct) is null) return new Outcome<string>.Refused(NotARole);
        var actor = await roles.Bag(ActorId, true, ct);
        if (ActorId == PersonId || !actor.InRole(Forum.Steward)) return new Outcome<string>.Refused(NotYours);

        if (Give) await roles.Add(Role, PersonId, ct: ct);
        else await roles.Remove(Role, PersonId, ct);
        await new LogEntry { Seq = await LogEntry.Log.Next(ct), What = $"role:{Role}:{PersonId}", Actor = ActorId }.Save(ct);

        if (person.Frozen) return new Outcome<string>.Refused(Frozen);
        if (Role == Forum.Doomed) throw new InvalidOperationException("The store fell over mid-change.");
        return new Outcome<string>.Done(Role);
    }
}

public sealed class VisitsController : ControllerBase
{
    /// <summary>What a form answers: sent on, with nothing to read. What success answers stays the action's.</summary>
    [HttpPost("/places/{place}/visits")]
    [Tool]
    public IActionResult Visit(string place)
    {
        Response.Headers.Location = $"/places/{place}";
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    [HttpDelete("/places/{place}/visits")]
    [Tool(particle: "forget")]
    public IActionResult Forget(string place, [FromBody] RoleInput reason, [FromQuery] int? count = null)
        => Ok(new { place, reason.Role, count });
}

public sealed record RoleInput(
    [Required, Description("The role.")] string? Role,
    [Description("Whether it is given; false takes it away.")] bool Give = true);

[Tool]
[Access(all: Koan.Web.Authorization.Access.Authenticated)]
public sealed class PeopleController : ControllerBase
{
    [HttpPost("/people/{person}/roles")]
    [Tool(Name = "change_role")]
    public async Task<IActionResult> Change(string person, RoleInput input, CancellationToken ct)
        => this.Answer(await new ChangeRole(Forum.PersonId(User)!, person, input.Role!, input.Give).Send(ct),
            role => Ok(new { person, role }));

    [HttpGet("/people/{person}/roles")]
    [Tool(particle: "of")]
    public async Task<IActionResult> Of(string person, CancellationToken ct)
        => Ok(await HttpContext.RequestServices.GetRequiredService<RoleCollection>().Bag(person, true, ct));
}

[Tool]
public sealed class AdminController : ControllerBase
{
    /// <summary>A role the caller's claims either hold or do not: the list can know.</summary>
    [HttpPost("/admin/sweep")]
    [Access(all: "is:admin")]
    [Description("Sweeps, for an administrator.")]
    public IActionResult Sweep() => Ok(new { swept = true });

    /// <summary>What only the application's own rule can decide.</summary>
    [HttpGet("/places/{place}/census")]
    public async Task<IActionResult> Census(string place, [FromQuery] int limit = 10, CancellationToken ct = default)
        => await Place.Get(place, ct) is null
            ? this.Answer<int>(new Outcome<int>.Refused(Reply.NoPlace), _ => Ok())
            : Ok(new { place, limit, posts = (await Post.Query(_ => true, ct)).Count });
}

public abstract class ShelfBase : ControllerBase
{
    [HttpPost("/shelf/inherited")]
    public IActionResult Inherited() => Ok(new { taken = true });
}

/// <summary>ASP.NET's own declarations on a tool: its middleware enforces them on either door.</summary>
[Tool]
[Authorize(Roles = "keeper")]
public sealed class KeptShelfController : ShelfBase
{
    [HttpPost("/shelf/own")]
    public IActionResult Own() => Ok(new { taken = true });

    [HttpPost("/shelf/open")]
    [AllowAnonymous]
    public IActionResult Open() => Ok(new { taken = true });
}

public abstract class LedgerBase : ControllerBase
{
    [HttpPost("/ledger/inherited")]
    public IActionResult Inherited() => Ok(new { read = true });
}

/// <summary>The gate on a derived controller reaches what it inherits.</summary>
[Tool]
[Access(all: "is:keeper")]
public sealed class KeptLedgerController : LedgerBase;

public abstract class VaultBase : ControllerBase
{
    [HttpPost("/vault/inherited")]
    public IActionResult Inherited() => Ok(new { opened = true });
}

[Tool]
[Authorize(Policy = Forum.NobodyPasses)]
public sealed class VaultController : VaultBase;

/// <summary>A class's declarations and a method's all hold.</summary>
[Authorize(Roles = "keeper")]
public sealed class StackedController : ControllerBase
{
    [HttpPost("/stack/scope"), Tool(Name = "stack_scope"), RequireScope("x")]
    public IActionResult Scoped() => Ok(new { ran = true });

    [HttpPost("/stack/roles"), Tool(Name = "stack_roles"), Authorize(Roles = "editor")]
    public IActionResult Roles() => Ok(new { ran = true });
}

[Access(all: "is:keeper")]
public sealed class GatedTwiceController : ControllerBase
{
    [HttpPost("/twice/scope"), Tool(Name = "twice_scope"), RequireScope("x")]
    public IActionResult Scoped() => Ok(new { ran = true });

    [HttpPost("/twice/role"), Tool(Name = "twice_role"), Access(all: "is:editor")]
    public IActionResult Role() => Ok(new { ran = true });
}

/// <summary>What a request is made of, on either door.</summary>
public sealed class FidelityController : ControllerBase
{
    [HttpGet("/fidelity/files/{name:regex(^[[a-z]]+$)}"), Tool(Name = "file_get")]
    public IActionResult File(string name) => Ok(new { name });

    [HttpGet("/fidelity/pages/{page=1}"), Tool(Name = "page_get")]
    public IActionResult Page(int page) => Ok(new { page });

    [HttpGet("/fidelity/search"), Tool(Name = "search_get")]
    public IActionResult Search([FromQuery(Name = "q")] string? search) => Ok(new { search });

    [HttpPost("/fidelity/many"), Tool(Name = "many_post")]
    public IActionResult Many([FromBody] List<string> items) => Ok(new { count = items.Count });

    [HttpPost("/fidelity/text"), Tool(Name = "text_post")]
    public IActionResult Text([FromBody] string text) => Ok(new { text });

    [HttpGet("/fidelity/tenant"), Tool(Name = "tenant_get")]
    public IActionResult Tenant([FromServices] TenantHolder holder) => Ok(new { scoped = holder.Tenant, item = HttpContext.Items["tenant"] });

    [HttpPost("/fidelity/timeout"), Tool(Name = "timeout_post")]
    public IActionResult TimesOut() => throw new TaskCanceledException("downstream timed out, secret=abc");

    [HttpPost("/fidelity/limited"), Tool(Name = "limited_post"), EnableRateLimiting(Forum.OnePermit)]
    public IActionResult Limited() => Ok(new { ran = true });

    [HttpGet("/fidelity/names/{name}"), Tool(Name = "name_get")]
    public IActionResult Name(string name) => Ok(new { name });

    [HttpGet("/fidelity/cat/{**rest}"), Tool(Name = "cat_get")]
    public IActionResult Cat(string rest) => Ok(new { rest });

    [HttpPost("/fidelity/items/{id}/archive"), Tool(Name = "item_archive")]
    public IActionResult Archive(string id) => Ok(new { ran = "archive", id });

    [HttpPost("/fidelity/items/{id}"), Tool(Name = "item_update")]
    public IActionResult Update(string id) => Ok(new { ran = "update", id });

    [HttpPost("/fidelity/items/special"), Tool(Name = "item_special")]
    public IActionResult Special() => Ok(new { ran = "special" });

    [HttpGet("/fidelity/since"), Tool(Name = "since_get")]
    public IActionResult Since([FromQuery] DateTimeOffset? since, [FromQuery] string? label) => Ok(new { since, label });

    [HttpGet("/fidelity/at/{at}"), Tool(Name = "at_get")]
    public IActionResult At(string at) => Ok(new { at });

    public sealed record Said(string? Body);

    [HttpPost("/fidelity/echo"), Tool(Name = "echo_post")]
    public IActionResult Echo(Said input) => Ok(new { body = input.Body });

    [HttpGet("/fidelity/stamp"), Tool(Name = "stamp_get")]
    public IActionResult Stamp() => Ok(new { at = "2026-11-01T10:00:00+02:00" });

    [HttpGet("/fidelity/list"), Tool(Name = "list_get")]
    public IActionResult List() => Ok(new[] { 1, 2, 3 });

    [HttpPost("/fidelity/signed-in"), Tool(Name = "signed_in_post"), Authorize]
    public IActionResult SignedIn() => Ok(new { signedIn = true });

    [HttpGet("/fidelity/whoami"), Tool(Name = "whoami_get")]
    public IActionResult WhoAmI() => Ok(new { id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") });
}

public sealed class TenantHolder
{
    public string? Tenant { get; set; }
}

/// <summary>The local process alone, and nobody signed in needs to be anyone.</summary>
public sealed class LocalController : ControllerBase
{
    [HttpPost("/local/check")]
    [Tool]
    [Access(all: "origin:local")]
    public IActionResult Check() => Ok(new { local = true });
}

public sealed class Article : Entity<Article>
{
    public string Title { get; set; } = "";
}

/// <summary>A whole RESTful surface: the entity's operations are its tools, and what the controller adds is a route tool, with a body
/// bound as <c>[ApiController]</c> binds one.</summary>
[Route("/data/articles")]
[Tool]
public sealed class ArticlesController : EntityController<Article>
{
    public sealed record Retitle([Description("The new title.")] string? Title, int Tries = 1);

    [HttpPost("{id}/retitle")]
    public async Task<IActionResult> Rename(string id, [FromBody] Retitle input, CancellationToken ct)
    {
        if (await Article.Get(id, ct) is not { } article) return NotFound();
        article.Title = input.Title ?? "";
        return Ok(await article.Save(ct));
    }

    [HttpGet("{id}/words")]
    [Tool(particle: "count")]
    public async Task<IActionResult> Words(string id, CancellationToken ct)
        => await Article.Get(id, ct) is { } article ? Ok(new { words = article.Title.Split(' ').Length }) : NotFound();
}

/// <summary>An application's own rule: hides the census from anyone flagged, knowable with no resource, and at a call hides the place named
/// "sealed", which only a resource can say, as a place that is not there.</summary>
public sealed class CensusRule : IAuthorizationProvider
{
    public int Order => 10;

    public Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        if (request.Resource is not MethodInfo { Name: nameof(AdminController.Census) }) return Task.FromResult<AuthorizeDecision?>(null);
        if (request.Subject.HasClaim("flagged", "true")) return Task.FromResult<AuthorizeDecision?>(AuthorizeDecision.Forbidden("flagged"));
        return Task.FromResult<AuthorizeDecision?>(request.Context?.TryGetValue("place", out var place) == true && Equals(place, "sealed")
            ? AuthorizeDecision.Hidden()
            : null);
    }
}

public static class Forum
{
    public const string Steward = "role:steward";
    public const string Member = "role:member";
    public const string Doomed = "role:doomed";
    public const string NobodyPasses = "nobody-passes";
    public const string OnePermit = "one-permit";

    public static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    /// <summary>The same words from the same author to the same place are one reply, on any door and at any retry.</summary>
    public static string Minted(string? author, string topic, string? parent, string? body)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', author, topic, parent, body))))[..26];
}
