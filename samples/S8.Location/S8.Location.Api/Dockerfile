FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet restore samples/S8.Location/S8.Location.Api/S8.Location.Api.csproj
RUN dotnet publish samples/S8.Location/S8.Location.Api/S8.Location.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 4914
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "S8.Location.Api.dll"]