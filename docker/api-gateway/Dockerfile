# =====================================================================
#  ApiGateway - multi-stage Docker build
#  Build context = repository ROOT:
#    docker build -f docker/api-gateway/Dockerfile -t api-gateway .
# =====================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/ApiGateway/ApiGateway.csproj src/ApiGateway/
RUN dotnet restore src/ApiGateway/ApiGateway.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/ApiGateway/ src/ApiGateway/
RUN dotnet publish src/ApiGateway/ApiGateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
