# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /out

# run
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:__PORT__
# Port is provided via ASPNETCORE_URLS/compose; no EXPOSE to keep template placeholder out
ENTRYPOINT ["dotnet", "TinyDockerApp.dll"]
