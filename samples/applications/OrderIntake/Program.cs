using Koan.Core;
using Koan.Core.Hosting.App;

[assembly: KoanApp(
    Name = "Order intake workload lab",
    Description = "Run bounded order intake against a named source and keep a verified durable receipt.",
    Tags = new[] { "sample", "data", "jobs", "providers" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
