# The sandbox: where everything runs that executes code a person hasn't merged yet.
# That is Claude's autopilot build and fix runs, and the PR's own restore, build,
# tests, lint and UI build. Each run gets a new container from this image, and the
# container is removed when the run ends (.github/scripts/sandbox.sh). It holds the
# toolchains the product needs, and nothing that reaches the host or GitHub: no
# Docker socket, no runner files, no GitHub token, no AUTOPILOT_PAT.
#
# Versions are pinned to what CI and the image build use: .NET 10, Node 22,
# pnpm 10.28.1, and the Claude Code version of the usage probe. dotnet-ef generates
# EF Core migrations (Npgsql.EntityFrameworkCore.PostgreSQL 10).
FROM node:22-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    PATH=/opt/dotnet-tools:$PATH
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
 && apt-get update \
 && apt-get install -y --no-install-recommends git jq \
 && rm -rf /var/lib/apt/lists/* \
 && npm install --global --no-fund --no-audit pnpm@10.28.1 @anthropic-ai/claude-code@2.1.68 \
 && dotnet tool install dotnet-ef --version 10.0.0 --tool-path /opt/dotnet-tools \
 && chmod -R a+rX /opt/dotnet-tools
# The image's unprivileged user (APP_UID, 1654 in the .NET images). sandbox.sh
# hands the run's directory to it.
USER $APP_UID
