@echo off
set PORT=__PORT__
echo Running TinyApp on port %PORT%
dotnet run --no-build --urls http://localhost:%PORT%
