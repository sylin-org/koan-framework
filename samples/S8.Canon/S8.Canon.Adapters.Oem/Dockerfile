FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet restore samples/S8.Canon/S8.Canon.Adapters.Oem/S8.Canon.Adapters.Oem.csproj
RUN dotnet publish samples/S8.Canon/S8.Canon.Adapters.Oem/S8.Canon.Adapters.Oem.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S8.Canon.Adapters.Oem.dll"]
