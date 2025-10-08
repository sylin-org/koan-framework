# -------------------------------
# Build stage
# -------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/S16.PantryPal/S16.PantryPal.csproj -c Release -o /out /p:UseAppHost=false

# -------------------------------
# Runtime stage (slim)
# -------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080 \
	DOTNET_EnableDiagnostics=0 \
	COMPlus_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "S16.PantryPal.dll"]
