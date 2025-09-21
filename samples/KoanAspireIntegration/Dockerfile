# Use the official .NET 9 runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Use the SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY samples/KoanAspireIntegration/KoanAspireIntegration.csproj samples/KoanAspireIntegration/
COPY src/ src/

# Restore packages
WORKDIR /src/samples/KoanAspireIntegration
RUN dotnet restore "KoanAspireIntegration.csproj"

# Copy source code and build
COPY samples/KoanAspireIntegration/ .
RUN dotnet build "KoanAspireIntegration.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "KoanAspireIntegration.csproj" -c Release -o /app/publish

# Create the final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Set environment variables for container mode
ENV ASPNETCORE_ENVIRONMENT=Development
ENV ASPNETCORE_URLS=http://+:8080
ENV Koan__Environment=Development
ENV Koan__Orchestration__Mode=External

ENTRYPOINT ["dotnet", "KoanAspireIntegration.dll"]