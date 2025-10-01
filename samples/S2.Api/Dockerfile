# S2.Api container (multi-stage)
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src
COPY . .
# Build using repository root as context; target S2.Api project path
RUN dotnet restore samples/S2.Api/S2.Api.csproj
RUN dotnet publish samples/S2.Api/S2.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5054
EXPOSE 5054
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S2.Api.dll"]
