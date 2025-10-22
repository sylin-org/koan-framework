# Build & publish S4.Web
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
# Only copy what the app needs to build: shared libs and the samples tree
COPY ./src ./src
COPY ./samples ./samples
RUN dotnet restore samples/S4.Web/S4.Web.csproj
RUN dotnet publish samples/S4.Web/S4.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5064
EXPOSE 5064
ENTRYPOINT ["dotnet","S4.Web.dll"]
