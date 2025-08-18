using Sora.Data.Core;
using Sora.Web;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();
builder.Services.AsWebApi();

// Bind Mongo from configuration/env (.env used by compose)
var cs = builder.Configuration.GetConnectionString("mongo")
         ?? Environment.GetEnvironmentVariable("MONGO__CONNECTION_STRING")
         ?? "mongodb://mongo:27017";

builder.Services.AddMongoAdapter(o => o.ConnectionString = cs);

builder.Services.AddSoraSwagger();

var app = builder.Build();

app.UseSoraWeb();
app.UseSoraSwagger();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/index.html"));

app.Run($"http://0.0.0.0:__PORT__");
