using Sora.Data.Core;
using Sora.Web;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();
builder.Services.AsWebApi();

builder.Services.AddSqliteAdapter(o => o.ConnectionString = "Data Source=./data/app.db");

builder.Services.AddSoraSwagger();

var app = builder.Build();

app.UseSoraWeb();
app.UseSoraSwagger();

app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run($"http://localhost:__PORT__");
