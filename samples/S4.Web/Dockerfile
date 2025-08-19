# Build & publish S4.Web
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . /src/samples/S4.Web
COPY ./src ./src
COPY ./samples ./samples
RUN dotnet publish samples/S4.Web/S4.Web.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:5064
EXPOSE 5064
ENTRYPOINT ["dotnet","S4.Web.dll"]
