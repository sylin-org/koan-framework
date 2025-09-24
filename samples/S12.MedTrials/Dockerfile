# Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/S12.MedTrials/S12.MedTrials.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5110
EXPOSE 5110
ENTRYPOINT ["dotnet", "S12.MedTrials.dll"]