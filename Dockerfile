# Paint-Ball Gameserver + Browser-Client – läuft hinter Caddy (TLS) im Docker-Netz "web"
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
WORKDIR /src

COPY Assets/Scripts/Core ./Assets/Scripts/Core
COPY server ./server
COPY web ./web
RUN dotnet publish server/Paintball.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runner
WORKDIR /app

COPY --from=builder /app ./
RUN mkdir -p /data && chown "$APP_UID" /data

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/api/health || exit 1

CMD ["dotnet", "Paintball.Server.dll", "--behind-proxy", "--public", "--http-port", "8080", "--data", "/data"]
