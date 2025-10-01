FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src
COPY . .
# Build whole repo (context at repo root) and publish target project
RUN dotnet restore samples/S8.Canon/S8.Canon.Api/S8.Canon.Api.csproj
RUN dotnet publish samples/S8.Canon/S8.Canon.Api/S8.Canon.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5108
EXPOSE 5108
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S8.Canon.Api.dll"]
