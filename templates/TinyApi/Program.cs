using Sora.Data.Core;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();
builder.Services.AsWebApi();
// JSON adapter is auto-discovered when referenced; explicit call optional
// builder.Services.AddJsonAdapter();

// Swagger auto-registers via Sora initializer

var app = builder.Build();
// Web pipeline is wired by Sora's startup filter (AddSora().AsWebApi()).

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run($"http://localhost:__PORT__");
