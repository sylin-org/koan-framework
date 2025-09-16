# S6.SocialCreator container (multi-stage)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# Copy entire repo to allow multi-project restore/publish
COPY . .
RUN dotnet restore Koan.sln
RUN dotnet publish samples/S6.SocialCreator/S6.SocialCreator.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# App port
ENV ASPNETCORE_URLS=http://+:5086
EXPOSE 5086

# Storage: use Local provider with a writable path inside container
# Use double underscore for nested config keys
ENV Koan__Storage__Profiles__local__Provider=local \
    Koan__Storage__Profiles__local__Container=media \
    Koan__Storage__Profiles__default__Provider=local \
    Koan__Storage__Profiles__default__Container=media \
    Koan__Storage__DefaultProfile=local \
    Koan__Storage__Providers__local__BasePath=/app/data

# Persist media content on a mounted volume
VOLUME ["/app/data"]

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "S6.SocialCreator.dll"]
