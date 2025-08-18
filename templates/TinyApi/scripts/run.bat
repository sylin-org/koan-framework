@echo off
set PORT=__PORT__
echo Running TinyApi on port %PORT%
dotnet run --no-build --urls http://localhost:%PORT%
