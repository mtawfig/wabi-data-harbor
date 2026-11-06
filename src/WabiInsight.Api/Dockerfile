# Wabi Insight API — multi-stage build. Build from the repository root:
#   docker build -f src/WabiInsight.Api/Dockerfile -t wabi-insight-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props WabiInsight.slnx .editorconfig ./
COPY src/WabiInsight.Core/WabiInsight.Core.csproj src/WabiInsight.Core/
COPY src/WabiInsight.Infrastructure/WabiInsight.Infrastructure.csproj src/WabiInsight.Infrastructure/
COPY src/WabiInsight.Api/WabiInsight.Api.csproj src/WabiInsight.Api/
RUN dotnet restore src/WabiInsight.Api/WabiInsight.Api.csproj
COPY src/ src/
RUN dotnet publish src/WabiInsight.Api/WabiInsight.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# QuestPDF/SkiaSharp need fontconfig and the native GL/font libraries; the embedded fonts cover Latin and Arabic.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 ca-certificates && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
# Persistent state: artifacts (reports, attachments) and the Data Protection key ring live on mounted volumes.
RUN mkdir -p /var/lib/wabi-insight/artifacts /var/lib/wabi-insight/keys && chown -R app:app /var/lib/wabi-insight
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__RootPath=/var/lib/wabi-insight/artifacts \
    DataProtection__KeyRingPath=/var/lib/wabi-insight/keys \
    DOTNET_EnableDiagnostics=0
USER app
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 CMD ["dotnet", "--version"]
ENTRYPOINT ["dotnet", "WabiInsight.Api.dll"]
