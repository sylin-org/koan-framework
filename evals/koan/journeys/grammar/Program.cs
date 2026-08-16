// The canonical Koan bootstrap, exactly as the skill teaches it: one composition point.
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
