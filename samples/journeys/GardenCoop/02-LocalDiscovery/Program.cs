using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;

[assembly: KoanApp(
    Name = "Garden Cooperative · Local Discovery",
    Description = "Garden operations plus local semantic discovery for the cooperative harvest.",
    Tags = new[] { "sample", "gardening", "local-ai", "semantic-search" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
