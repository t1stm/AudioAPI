# Accounts, and the playlists that hang off them — no ffmpeg, no yt-dlp, no platform state, and no
# call to any other service. The one service in the stack whose data must outlive the container:
# mount Dom__DataFile's directory. See PLAYLISTS_PLAN.md and compose.yaml.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Dom/Dom.csproj Dom/
RUN dotnet restore Dom/Dom.csproj

COPY Dom/ Dom/
RUN dotnet publish Dom/Dom.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Dom.dll"]
