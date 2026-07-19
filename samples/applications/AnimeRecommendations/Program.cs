using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;

[assembly: KoanApp(
    Name = "Anime Recommendations",
    Description = "Rate what you love and discover anime that fits your taste and mood.",
    Tags = new[] { "sample", "recommendations", "local-ai", "semantic-search" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
