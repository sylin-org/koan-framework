using Koan.Core;
using Koan.Core.Hosting.App;

[assembly: KoanApp(
    Name = "Garden Cooperative (Embedded)",
    Description = "Search local produce with in-process SQLite, sqlite-vec, and ONNX.",
    Tags = new[] { "sample", "embedded", "semantic-search" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
