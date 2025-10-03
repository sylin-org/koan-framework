using Koan.Data.Core;
using Koan.Web.Connector.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
builder.Services.AsWebApi();

builder.Services.AddSqliteAdapter(o => o.ConnectionString = "Data Source=./data/app.db");

// Swagger auto-registers via Koan initializer

var app = builder.Build();
// Web pipeline is wired by Koan's startup filter (AddKoan().AsWebApi()).

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run($"http://localhost:__PORT__");

