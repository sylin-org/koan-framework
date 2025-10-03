# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/S5.Recs/S5.Recs.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5084
EXPOSE 5084
ENTRYPOINT ["dotnet", "S5.Recs.dll"]
