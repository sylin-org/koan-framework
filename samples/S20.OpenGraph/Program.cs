using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Web.OpenGraph;

// S20.OpenGraph: the smallest app that demonstrates declarative social cards.
// Run it, then:
//   curl -H "Accept: text/html" http://localhost:5080/notes/welcome
//   curl -H "Accept: text/html" http://localhost:5080/notes/unknown   (default card)
//   curl -H "Accept: text/html" http://localhost:5080/                 (default card)

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();

// Point the pillar at the static shell and supply brand defaults.
builder.Configuration["Koan:Web:OpenGraph:ShellPath"] = "wwwroot/index.html";
builder.Configuration["Koan:Web:OpenGraph:SiteName"] = "Koan OpenGraph Sample";
builder.Configuration["Koan:Web:OpenGraph:DefaultImage"] = "/img/default-card.png";
builder.Configuration["Koan:Web:OpenGraph:DefaultDescription"] = "A tiny Koan app with per-route social cards.";

// Declare one card. The route template's single token resolves to a Note; the selectors
// project the card fields. A trailing slug segment (e.g. /notes/welcome/hello-world) is
// matched and discarded.
SocialCards
    .For<Note>("/notes/{id}", id => Note.Get(id))
        .Title(n => n.Title)
        .Description(n => n.Body)
        .Image(n => CardImage.Recipe("share-card", n.CoverMediaId))
        .Url(n => $"/notes/{n.Id}")
        .Type("article");

var app = builder.Build();

// Seed one note once the host (and its ambient data context) is up.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var note = new Note
    {
        Id = "welcome",
        Title = "Welcome to the OpenGraph sample",
        Body = "Share this link in Discord or Slack and it unfurls with this note's title and description.",
        CoverMediaId = "welcome-cover",
    };
    note.Save().GetAwaiter().GetResult();
});

app.UseStaticFiles();

// One line, ahead of the SPA fallback: inject the per-route card on HTML navigations.
app.UseOpenGraphCards();
app.MapFallbackToFile("index.html");

app.Run();

public sealed class Note : Entity<Note>
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? CoverMediaId { get; set; }
}
