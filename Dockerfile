# Fitzobit Delivery Management: container image for Render (or any Docker host).
# Build: docker build -t fitzobit .    Run: see deploy/RENDER.md for the required environment variables.

# 1. UI (React + Vite) -> src/Fitzobit.Web/wwwroot
FROM node:20-alpine AS ui
WORKDIR /src/src/Fitzobit.UI
COPY src/Fitzobit.UI/package.json src/Fitzobit.UI/package-lock.json ./
RUN npm ci
COPY src/Fitzobit.UI/ ./
RUN npm run build

# 2. Server (ASP.NET Core 8)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Fitzobit.sln ./
COPY src/Fitzobit.Domain/Fitzobit.Domain.csproj src/Fitzobit.Domain/
COPY src/Fitzobit.Application/Fitzobit.Application.csproj src/Fitzobit.Application/
COPY src/Fitzobit.Infrastructure/Fitzobit.Infrastructure.csproj src/Fitzobit.Infrastructure/
COPY src/Fitzobit.Infrastructure.PostgreSql/Fitzobit.Infrastructure.PostgreSql.csproj src/Fitzobit.Infrastructure.PostgreSql/
COPY src/Fitzobit.Web/Fitzobit.Web.csproj src/Fitzobit.Web/
RUN dotnet restore src/Fitzobit.Web/Fitzobit.Web.csproj
COPY src/ src/
COPY Fitzobit_WhatsApp_Catalogue.xlsx ./
COPY --from=ui /src/src/Fitzobit.Web/wwwroot src/Fitzobit.Web/wwwroot
RUN dotnet publish src/Fitzobit.Web/Fitzobit.Web.csproj -c Release -o /app --no-restore

# 3. Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app ./
# Small instances (Render free: 512 MB, 0.1 CPU): workstation GC keeps memory low.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapHardLimitPercent=0x4B \
    Fitzobit__BasePath=/app \
    PORT=10000
USER app
EXPOSE 10000
# Render provides PORT; listen on all interfaces inside the container.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT} exec dotnet Fitzobit.Web.dll"]
