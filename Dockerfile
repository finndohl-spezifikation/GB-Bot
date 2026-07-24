# ── Build-Stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o out

# ── Run-Stage ─────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:8.0

WORKDIR /app
COPY --from=builder /app/out .
COPY logo.jpg /app/logo.jpg

CMD ["dotnet", "CleanYourServer.dll"]
