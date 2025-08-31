FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore samples/S8.Flow.Adapters.Oem/S8.Flow.Adapters.Oem.csproj
RUN dotnet publish samples/S8.Flow.Adapters.Oem/S8.Flow.Adapters.Oem.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S8.Flow.Adapters.Oem.dll"]
