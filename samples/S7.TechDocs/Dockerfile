# syntax=docker/dockerfile:1

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src

# Copy everything and restore/build only the S7 project to speed up context use
COPY . .
WORKDIR /src/samples/S7.TechDocs
RUN dotnet restore S7.TechDocs.csproj
RUN dotnet publish S7.TechDocs.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "S7.TechDocs.dll"]
