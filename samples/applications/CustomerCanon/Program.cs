using Koan.Core;
using Koan.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKoan()
    .AsWebApi();

var app = builder.Build();

await app.RunAsync();
