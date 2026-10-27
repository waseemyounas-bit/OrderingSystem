# =====================================================================
#  PaymentService - multi-stage Docker build
#  Build context = repository ROOT (because we also need BuildingBlocks):
#    docker build -f docker/payment-service/Dockerfile -t payment-service .
# =====================================================================

# ---------- Stage 1: build (big image with the full .NET SDK) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy ONLY project files first and restore. Docker caches this layer, so NuGet packages
# are re-downloaded only when a .csproj changes - not on every code change.
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/Services/PaymentService/PaymentService.csproj src/Services/PaymentService/
RUN dotnet restore src/Services/PaymentService/PaymentService.csproj

# Now copy the source code and publish.
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/PaymentService/ src/Services/PaymentService/
RUN dotnet publish src/Services/PaymentService/PaymentService.csproj -c Release -o /app/publish --no-restore

# ---------- Stage 2: runtime (small image, only the ASP.NET runtime) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8+ images listen on port 8080 and ship a non-root "app" user.
# Running as non-root is a Kubernetes security best practice (runAsNonRoot).
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PaymentService.dll"]
