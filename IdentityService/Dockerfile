# ============================================================
# Stage 1: Build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy solution and project files first (for layer caching)
COPY IdentityService.slnx ./
COPY src/IdentityService.API/IdentityService.API.csproj ./src/IdentityService.API/
COPY src/IdentityService.Core/IdentityService.Core.csproj ./src/IdentityService.Core/
COPY src/IdentityService.Infrastructure/IdentityService.Infrastructure.csproj ./src/IdentityService.Infrastructure/
COPY tests/IdentityService.Tests/IdentityService.Tests.csproj ./tests/IdentityService.Tests/

# Restore NuGet packages
RUN dotnet restore src/IdentityService.API/IdentityService.API.csproj

# Copy all source code
COPY src/ ./src/
COPY tests/ ./tests/

# Build and publish the API project
RUN dotnet publish src/IdentityService.API/IdentityService.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser

# Copy published files
COPY --from=build /app/publish .

# Set ownership
RUN chown -R appuser:appgroup /app

USER appuser

# Expose HTTP port
EXPOSE 8080

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Docker
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "IdentityService.API.dll"]
