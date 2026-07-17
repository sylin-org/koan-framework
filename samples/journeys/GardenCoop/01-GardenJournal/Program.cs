using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;

[assembly: KoanApp(
    Name = "Garden Cooperative",
    Description = "Neighborhood produce co-op slice showcasing Koan self-description.",
    Tags = new[] { "sample", "gardening" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
