using Koan.Data.Core;
using Koan.Web.Connector.Swagger;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
builder.Services.AsWebApi();

// Bind Mongo from configuration/env (.env used by compose)
var cs = builder.Configuration.GetConnectionString("mongo")
         ?? Environment.GetEnvironmentVariable("MONGO__CONNECTION_STRING")
         ?? "mongodb://mongo:27017";

builder.Services.AddMongoAdapter(o => o.ConnectionString = cs);

// Swagger auto-registers via Koan initializer

var app = builder.Build();
// Web pipeline is wired by Koan's startup filter (AddKoan().AsWebApi()).

app.Run($"http://0.0.0.0:__PORT__");

