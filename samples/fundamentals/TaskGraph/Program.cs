using Koan.Core;
using Koan.Core.Hosting.App;

[assembly: KoanApp(
    Name = "Tasks and relationships",
    Description = "Business-readable entities with scalar, set, and streaming relationship context.",
    Tags = new[] { "sample", "relationships", "cache" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
