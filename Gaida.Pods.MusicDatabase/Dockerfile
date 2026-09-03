# Build context is the repo root (see compose.yaml) -- project-references Gaida.Core and the
# MusicDatabase platform library.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gaida.Pods.MusicDatabase/Gaida.Pods.MusicDatabase.csproj Gaida.Pods.MusicDatabase/
COPY Gaida/Gaida.Core/Gaida.Core.csproj Gaida/Gaida.Core/
COPY Gaida/Platforms/Gaida.Platforms.MusicDatabase/Gaida.Platforms.MusicDatabase.csproj Gaida/Platforms/Gaida.Platforms.MusicDatabase/
RUN dotnet restore Gaida.Pods.MusicDatabase/Gaida.Pods.MusicDatabase.csproj

COPY Gaida.Pods.MusicDatabase/ Gaida.Pods.MusicDatabase/
COPY Gaida/Gaida.Core/ Gaida/Gaida.Core/
COPY Gaida/Platforms/Gaida.Platforms.MusicDatabase/ Gaida/Platforms/Gaida.Platforms.MusicDatabase/
RUN dotnet publish Gaida.Pods.MusicDatabase/Gaida.Pods.MusicDatabase.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# MusicManager.Manager/MediaInfo.cs shells out to ffprobe to read audio tags.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gaida.Pods.MusicDatabase.dll"]
