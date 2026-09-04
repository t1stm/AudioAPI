# Build context is the repo root (see compose.yaml) -- project-references the Spotify platform library
# (Gaida/Platforms/Gaida.Platforms.Spotify) and, transitively, Gaida.Core.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj Gaida.Pods.Spotify/
COPY Gaida/Platforms/Gaida.Platforms.Spotify/Gaida.Platforms.Spotify.csproj Gaida/Platforms/Gaida.Platforms.Spotify/
COPY Gaida/Gaida.Core/Gaida.Core.csproj Gaida/Gaida.Core/
RUN dotnet restore Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj

COPY Gaida.Pods.Spotify/ Gaida.Pods.Spotify/
COPY Gaida/Platforms/Gaida.Platforms.Spotify/ Gaida/Platforms/Gaida.Platforms.Spotify/
COPY Gaida/Gaida.Core/ Gaida/Gaida.Core/
RUN dotnet publish Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gaida.Pods.Spotify.dll"]
