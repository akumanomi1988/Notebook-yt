FROM node:24-alpine AS web
WORKDIR /build/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY src/ src/
RUN dotnet publish src/NotebookYt.Api/NotebookYt.Api.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg fonts-dejavu-core && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/notebook-yt/media /var/lib/notebook-yt/keys && chown -R 1654:1654 /var/lib/notebook-yt
WORKDIR /app
COPY --from=build /publish/ ./
COPY --from=web /build/web/dist/ ./wwwroot/
ENV Hosting__Urls=http://0.0.0.0:8080 Storage__Root=/var/lib/notebook-yt/media Security__KeyDirectory=/var/lib/notebook-yt/keys
USER 1654
ENTRYPOINT ["dotnet", "NotebookYt.Api.dll"]
