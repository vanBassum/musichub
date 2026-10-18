# Single image: the Vite UI is built and served as static files by the ASP.NET Core API.
#   docker build -t musichub .
#   docker run -p 8080:8080 musichub

# -----------------------------
# UI build stage
# -----------------------------
FROM node:22-alpine AS ui-build
WORKDIR /ui
RUN corepack enable && corepack prepare pnpm@10.28.1 --activate

COPY musichub-ui/package.json musichub-ui/pnpm-lock.yaml musichub-ui/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY musichub-ui/ ./
# Build identity from .github/workflows/build.yml, baked into the bundle by Vite.
ARG APP_COMMIT=unknown
ARG APP_BUILD_TIME=unknown
ENV VITE_APP_COMMIT=$APP_COMMIT \
    VITE_APP_BUILD_TIME=$APP_BUILD_TIME
RUN pnpm build

# -----------------------------
# API build stage
# -----------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY musichub-api/MusicHub.Api.csproj musichub-api/
RUN dotnet restore musichub-api/MusicHub.Api.csproj

COPY musichub-api/ musichub-api/
RUN dotnet publish musichub-api/MusicHub.Api.csproj -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# -----------------------------
# Final runtime image
# -----------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# fpcalc (Chromaprint), run by the AcoustID audio fingerprint provider.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libchromaprint-tools \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

COPY --from=api-build /app/publish .
COPY --from=ui-build /ui/dist ./wwwroot

# Build identity, set by .github/workflows/build.yml (commit date and 12-character
# SHA). Defaults so a local `docker build` without the CI args still runs.
ARG APP_VERSION=dev
ARG APP_COMMIT=unknown
ENV APP_VERSION=$APP_VERSION \
    APP_COMMIT=$APP_COMMIT

ENTRYPOINT ["dotnet", "MusicHub.Api.dll"]
