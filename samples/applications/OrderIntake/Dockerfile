# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0.100 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ./samples/applications/OrderIntake/OrderIntake.csproj -c Release -o /out

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:5174
EXPOSE 5174
ENTRYPOINT ["dotnet", "OrderIntake.dll"]
