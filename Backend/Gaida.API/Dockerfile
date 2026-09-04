# Build context is the repo root (see compose.yaml) -- project-references Gaida.Core only. Every
# platform is a pod reached over HTTP now (HttpPlatform, Gaida.Core/Platforms/HttpPlatform.cs).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Gaida.API/Gaida.API.csproj Gaida.API/
COPY Gaida/Gaida.Core/Gaida.Core.csproj Gaida/Gaida.Core/
RUN dotnet restore Gaida.API/Gaida.API.csproj

COPY Gaida.API/ Gaida.API/
COPY Gaida/Gaida.Core/ Gaida/Gaida.Core/
RUN dotnet publish Gaida.API/Gaida.API.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# FFmpegEncoder.cs shells out to ffmpeg on PATH for every /Audio/Download.
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gaida.API.dll"]
