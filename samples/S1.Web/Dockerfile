# S1.Web container (multi-stage)
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
WORKDIR /src
# Copy entire Koan folder (multi-project) as build context
COPY . .
# Restore and publish using Koan root as build context
RUN dotnet restore Koan.sln
RUN dotnet publish samples/S1.Web/S1.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:5044
# Declare data directory as a volume to persist/mount on host
VOLUME ["/app/data"]
EXPOSE 5044
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet","S1.Web.dll"]
