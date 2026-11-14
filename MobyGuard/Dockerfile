# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM node:lts-alpine AS ui

WORKDIR /web

COPY web/package*.json ./

RUN --mount=type=cache,id=npm,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY web/ ./

RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
SHELL ["/bin/bash", "-c"]
ARG TARGETARCH

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src

COPY MobyGuard/*.csproj ./MobyGuard/

RUN --mount=type=cache,id=nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet restore ./MobyGuard/MobyGuard.csproj -a "${TARGETARCH/amd64/x64}" --use-current-runtime

COPY MobyGuard/ ./MobyGuard/
COPY --from=ui /MobyGuard/wwwroot ./MobyGuard/wwwroot/

RUN --mount=type=cache,id=nuget,sharing=locked,target=/root/.nuget/packages \
    dotnet publish ./MobyGuard/MobyGuard.csproj \
      -c Release \
      -a "${TARGETARCH/amd64/x64}" \
      --use-current-runtime \
      --self-contained false \
      --no-restore \
      -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/certs /app/data \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/out .

USER app

EXPOSE 5090

ENTRYPOINT ["dotnet", "MobyGuard.dll"]
