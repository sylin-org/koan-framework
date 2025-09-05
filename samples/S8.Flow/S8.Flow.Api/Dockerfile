FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
# Build whole repo (context at repo root) and publish target project
RUN dotnet restore samples/S8.Flow/S8.Flow.Api/S8.Flow.Api.csproj
RUN dotnet publish samples/S8.Flow/S8.Flow.Api/S8.Flow.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5108
EXPOSE 5108
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S8.Flow.Api.dll"]
