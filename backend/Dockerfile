# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY JuDesk.sln ./
COPY src/JuDesk.Domain/JuDesk.Domain.csproj src/JuDesk.Domain/
COPY src/JuDesk.Api/JuDesk.Api.csproj src/JuDesk.Api/
RUN dotnet restore src/JuDesk.Api/JuDesk.Api.csproj
COPY src/ src/
RUN dotnet publish src/JuDesk.Api/JuDesk.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# Data-protection keys (session cookies) live here when DataProtection__KeysPath=/keys; compose mounts a volume.
RUN mkdir /keys && chown app:app /keys
# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
# No curl in the runtime image: bash's /dev/tcp is enough for a health probe.
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\n\r\n" >&3 && grep -q Healthy <&3' || exit 1
ENTRYPOINT ["dotnet", "JuDesk.Api.dll"]
