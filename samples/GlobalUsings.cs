// Shared global usings for sample projects that are framework-agnostic.
// Web-specific (AspNetCore) usings were removed to allow Worker/Console projects
// (e.g., S9.Location adapters, seed tasks) to build without pulling in the full ASP.NET Core shared framework.
// Individual web projects rely on implicit usings from the Web SDK or declare their own as needed.
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
