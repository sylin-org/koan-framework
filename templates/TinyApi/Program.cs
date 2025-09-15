using Koan.Data.Core;
using Koan.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
builder.Services.AsWebApi();
// JSON adapter is auto-discovered when referenced; explicit call optional
// builder.Services.AddJsonAdapter();

// Swagger auto-registers via Koan initializer

var app = builder.Build();
// Web pipeline is wired by Koan's startup filter (AddKoan().AsWebApi()).

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run($"http://localhost:__PORT__");
