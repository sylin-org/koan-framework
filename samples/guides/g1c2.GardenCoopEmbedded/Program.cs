using GardenCoopEmbedded;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;

[assembly: KoanApp(
    Name = "Garden Cooperative (Embedded)",
    Description = "Single-binary co-op slice: in-process data, vectors, embeddings, and messaging — no container, no servers.",
    Tags = new[] { "sample", "embedded", "sovereign" }
)]

var builder = WebApplication.CreateBuilder(args);

// One line wires everything by Reference = Intent: SQLite data, sqlite-vec vectors, ONNX embeddings,
// and the in-process Channels bus are all selected purely by which packages this project references.
builder.Services.AddKoan();

var app = builder.Build();
AppHost.Current ??= app.Services;

// Start the host first so hosted services run — including the one that registers the in-process ONNX
// embedder — then seed (which needs the embedder), then run until shutdown.
await app.StartAsync();
await Seed.EnsureAsync();   // embeds each listing (ONNX) and stores the vector (sqlite-vec)
await app.WaitForShutdownAsync();
