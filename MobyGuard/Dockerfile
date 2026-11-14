FROM --platform=$BUILDPLATFORM node:lts-alpine AS ui

WORKDIR /web

COPY web/package*.json ./

RUN npm install --no-audit --no-fund

COPY web/ ./

RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY MobyGuard/*.csproj ./MobyGuard/

RUN dotnet restore ./MobyGuard/MobyGuard.csproj

COPY MobyGuard/ ./MobyGuard/
COPY --from=ui /MobyGuard/wwwroot ./MobyGuard/wwwroot/

RUN dotnet publish ./MobyGuard/MobyGuard.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0

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
