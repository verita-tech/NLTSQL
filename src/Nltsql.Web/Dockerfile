# Build stage -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore against the manifests alone, so the layer is reused whenever
# only source files changed.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Nltsql.Core/Nltsql.Core.csproj src/Nltsql.Core/
COPY src/Nltsql.Infrastructure/Nltsql.Infrastructure.csproj src/Nltsql.Infrastructure/
COPY src/Nltsql.Web/Nltsql.Web.csproj src/Nltsql.Web/
RUN dotnet restore src/Nltsql.Web/Nltsql.Web.csproj

COPY src/ src/
RUN dotnet publish src/Nltsql.Web/Nltsql.Web.csproj -c Release -o /app --no-restore

# Runtime stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root: the app needs no privileges beyond its own data directory.
RUN useradd --uid 64198 --create-home app \
 && mkdir -p /data \
 && chown -R app:app /data
USER app

COPY --from=build --chown=app:app /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "Nltsql.Web.dll"]
