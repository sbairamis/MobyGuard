# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM node:lts-alpine AS ui

WORKDIR /web

COPY web/package*.json ./

RUN --mount=type=cache,id=npm,target=/root/.npm \
    npm ci --no-audit --no-fund

COPY web/ ./

RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
SHELL ["/bin/sh", "-c"]
ARG TARGETARCH
ARG APP_VERSION=""

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src

COPY MobyGuard/*.csproj ./MobyGuard/

RUN dotnet restore ./MobyGuard/MobyGuard.csproj -a "${TARGETARCH/amd64/x64}" --use-current-runtime

COPY MobyGuard/ ./MobyGuard/

RUN dotnet publish ./MobyGuard/MobyGuard.csproj \
      -c Release \
      -a "${TARGETARCH/amd64/x64}" \
      --use-current-runtime \
      --self-contained false \
      --no-restore \
      ${APP_VERSION:+-p:Version=${APP_VERSION#v}} \
      -o /app/out \
    && cp ./MobyGuard/appsettings.example.json /app/out/appsettings.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

WORKDIR /app

ARG UID=10001
RUN adduser \
    --disabled-password \
    --gecos "" \
    --home "/nonexistent" \
    --shell "/sbin/nologin" \
    --no-create-home \
    --uid "${UID}" \
    appuser \
    && apk add --no-cache curl \
    && mkdir -p /app/certs /app/data \
    && chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/out .
COPY --from=ui --chown=appuser:appuser /MobyGuard/wwwroot ./wwwroot

USER appuser

EXPOSE 5090

ENTRYPOINT ["dotnet", "MobyGuard.dll"]
