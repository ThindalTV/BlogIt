# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY BlogIt.sln .
COPY BlogIt.Shared/BlogIt.Shared.csproj BlogIt.Shared/
COPY BlogIt.ServiceDefaults/BlogIt.ServiceDefaults.csproj BlogIt.ServiceDefaults/
COPY BlogIt.Web/BlogIt.Web.csproj BlogIt.Web/
COPY BlogIt.AdminSite/BlogIt.AdminSite.csproj BlogIt.AdminSite/

RUN dotnet restore BlogIt.Web/BlogIt.Web.csproj

# Copy all source
COPY BlogIt.Shared/ BlogIt.Shared/
COPY BlogIt.ServiceDefaults/ BlogIt.ServiceDefaults/
COPY BlogIt.Web/ BlogIt.Web/
COPY BlogIt.AdminSite/ BlogIt.AdminSite/

# Publish the web host (this also builds the WASM AdminSite via ProjectReference)
RUN dotnet publish BlogIt.Web/BlogIt.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "BlogIt.Web.dll"]
