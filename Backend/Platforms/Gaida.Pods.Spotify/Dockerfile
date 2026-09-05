# Build context is the repo root (see compose.yaml) -- project-references the Spotify platform library
# (Platforms/Gaida.Platforms.Spotify) and, transitively, Gaida.Core.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Platforms/Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj", "Platforms/Gaida.Pods.Spotify/"]
COPY ["Platforms/Gaida.Platforms.Spotify/Gaida.Platforms.Spotify.csproj", "Platforms/Gaida.Platforms.Spotify/"]
COPY ["Gaida Library/Gaida.Core/Gaida.Core.csproj", "Gaida Library/Gaida.Core/"]
COPY ["Gaida Library/Gaida.Admin/Gaida.Admin.csproj", "Gaida Library/Gaida.Admin/"]
RUN dotnet restore "Platforms/Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj"

COPY ["Platforms/Gaida.Pods.Spotify/", "Platforms/Gaida.Pods.Spotify/"]
COPY ["Platforms/Gaida.Platforms.Spotify/", "Platforms/Gaida.Platforms.Spotify/"]
COPY ["Gaida Library/Gaida.Core/", "Gaida Library/Gaida.Core/"]
COPY ["Gaida Library/Gaida.Admin/", "Gaida Library/Gaida.Admin/"]
RUN dotnet publish "Platforms/Gaida.Pods.Spotify/Gaida.Pods.Spotify.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gaida.Pods.Spotify.dll"]
