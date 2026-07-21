using Koan.Core;
using Koan.Core.Hosting.App;

[assembly: KoanApp(
    Name = "Developer publishing portal",
    Description = "Approve articles locally and publish them through named provider channels.",
    Tags = new[] { "sample", "data", "providers", "transfers" }
)]

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public partial class Program;
