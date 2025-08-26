using Sora.Data.Core;
using Sora.Web.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora();
builder.Services.AsWebApi();

// Bind Mongo from configuration/env (.env used by compose)
var cs = builder.Configuration.GetConnectionString("mongo")
         ?? Environment.GetEnvironmentVariable("MONGO__CONNECTION_STRING")
         ?? "mongodb://mongo:27017";

builder.Services.AddMongoAdapter(o => o.ConnectionString = cs);

// Swagger auto-registers via Sora initializer

var app = builder.Build();
// Web pipeline is wired by Sora's startup filter (AddSora().AsWebApi()).

app.Run($"http://0.0.0.0:__PORT__");
