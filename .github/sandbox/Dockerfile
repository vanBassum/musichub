# The sandbox: where a PR's own restore, build, tests, lint and UI build run. Each
# run gets a new container from this image, and the container is removed when the
# run ends (.github/scripts/sandbox.sh). It holds the toolchains the product needs,
# and nothing that reaches the host or GitHub: no Docker socket, no runner files,
# no GitHub token.
#
# Versions are pinned to what the image build uses: .NET 10, Node 22, pnpm 10.28.1.
FROM node:22-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
COPY --from=node /usr/local/bin/node /usr/local/bin/node
COPY --from=node /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s ../lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && ln -s ../lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
 && apt-get update \
 && apt-get install -y --no-install-recommends git \
 && rm -rf /var/lib/apt/lists/* \
 && npm install --global --no-fund --no-audit pnpm@10.28.1
# The image's unprivileged user (APP_UID, 1654 in the .NET images). sandbox.sh
# hands the run's directory to it.
USER $APP_UID
