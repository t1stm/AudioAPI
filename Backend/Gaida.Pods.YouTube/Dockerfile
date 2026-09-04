# Build context is the repo root (see compose.yaml) -- project-references the YouTube platform
# library (Gaida/Platforms/Gaida.Platforms.YouTube) and, transitively, Gaida.Core.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gaida.Pods.YouTube/Gaida.Pods.YouTube.csproj Gaida.Pods.YouTube/
COPY Gaida/Platforms/Gaida.Platforms.YouTube/Gaida.Platforms.YouTube.csproj Gaida/Platforms/Gaida.Platforms.YouTube/
COPY Gaida/Gaida.Core/Gaida.Core.csproj Gaida/Gaida.Core/
RUN dotnet restore Gaida.Pods.YouTube/Gaida.Pods.YouTube.csproj

COPY Gaida.Pods.YouTube/ Gaida.Pods.YouTube/
COPY Gaida/Platforms/Gaida.Platforms.YouTube/ Gaida/Platforms/Gaida.Platforms.YouTube/
COPY Gaida/Gaida.Core/ Gaida/Gaida.Core/
RUN dotnet publish Gaida.Pods.YouTube/Gaida.Pods.YouTube.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# GetterYtDlp (Gaida.Core/Platforms/Cross Platform/Getter_YtDLP.cs) shells out to yt-dlp on PATH.
# The standalone Linux binary bundles its own Python, so no system python3 is needed.
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && apt-get purge -y curl && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gaida.Pods.YouTube.dll"]
