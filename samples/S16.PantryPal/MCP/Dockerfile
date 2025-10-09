# Build MCP Host
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/S16.PantryPal.McpHost/S16.PantryPal.McpHost.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5026
EXPOSE 5026
ENTRYPOINT ["dotnet", "S16.PantryPal.McpHost.dll"]
