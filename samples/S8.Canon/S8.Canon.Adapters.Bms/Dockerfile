FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src
COPY . .
RUN dotnet restore samples/S8.Canon/S8.Canon.Adapters.Bms/S8.Canon.Adapters.Bms.csproj
RUN dotnet publish samples/S8.Canon/S8.Canon.Adapters.Bms/S8.Canon.Adapters.Bms.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S8.Canon.Adapters.Bms.dll"]
