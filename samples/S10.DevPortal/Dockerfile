# S10.DevPortal Dockerfile - Multi-stage build for efficient container size
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5090

# Create data directory for SQLite
RUN mkdir -p /app/data && chmod 755 /app/data

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY ["samples/S10.DevPortal/S10.DevPortal.csproj", "samples/S10.DevPortal/"]

# Copy framework source projects
COPY ["src/Koan.Core/Koan.Core.csproj", "src/Koan.Core/"]
COPY ["src/Koan.Web/Koan.Web.csproj", "src/Koan.Web/"]
COPY ["src/Koan.Web.Extensions/Koan.Web.Extensions.csproj", "src/Koan.Web.Extensions/"]
COPY ["src/Koan.Data.Core/Koan.Data.Core.csproj", "src/Koan.Data.Core/"]
COPY ["src/Koan.Data.Mongo/Koan.Data.Mongo.csproj", "src/Koan.Data.Mongo/"]
COPY ["src/Koan.Data.Postgres/Koan.Data.Postgres.csproj", "src/Koan.Data.Postgres/"]
COPY ["src/Koan.Data.Sqlite/Koan.Data.Sqlite.csproj", "src/Koan.Data.Sqlite/"]

# Restore dependencies
RUN dotnet restore "samples/S10.DevPortal/S10.DevPortal.csproj"

# Copy source code
COPY . .

# Build the application
WORKDIR "/src/samples/S10.DevPortal"
RUN dotnet build "S10.DevPortal.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "S10.DevPortal.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables for container operation
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:5090
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Add health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5090/api/health || exit 1

ENTRYPOINT ["dotnet", "S10.DevPortal.dll"]