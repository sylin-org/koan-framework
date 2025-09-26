FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 5125

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore dependencies
COPY ["samples/S13.DocMind/S13.DocMind.csproj", "samples/S13.DocMind/"]
RUN dotnet restore "samples/S13.DocMind/S13.DocMind.csproj"

# Copy source code
COPY . .
WORKDIR "/src/samples/S13.DocMind"

# Build application
RUN dotnet build "S13.DocMind.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "S13.DocMind.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "S13.DocMind.dll"]
