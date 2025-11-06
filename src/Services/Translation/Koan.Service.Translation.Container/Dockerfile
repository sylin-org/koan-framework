# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./src/Koan.Service.Translation.Container/Koan.Service.Translation.Container.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .

# Environment variables for configuration
ENV ASPNETCORE_URLS=http://+:8080
ENV Koan__Service__Translation__Port=8080
ENV Koan__Service__Orchestrator__MulticastGroup=239.255.42.1
ENV Koan__Service__Orchestrator__MulticastPort=42001

EXPOSE 8080
ENTRYPOINT ["dotnet", "Koan.Service.Translation.Container.dll"]
