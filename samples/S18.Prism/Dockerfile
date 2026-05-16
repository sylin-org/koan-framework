# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/S18.Prism/S18.Prism.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .

# Create volume mount points
RUN mkdir -p /app/storage /app/cache /app/data

ENV ASPNETCORE_URLS=http://+:5087
EXPOSE 5087
ENTRYPOINT ["dotnet", "S18.Prism.dll"]
