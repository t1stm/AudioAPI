# Build context is the repo root (see compose.yaml) -- Dunav project-references Gaida.Core.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Dunav/Dunav.csproj Dunav/
COPY Gaida/Gaida.Core/Gaida.Core.csproj Gaida/Gaida.Core/
RUN dotnet restore Dunav/Dunav.csproj

COPY Dunav/ Dunav/
COPY Gaida/Gaida.Core/ Gaida/Gaida.Core/
RUN dotnet publish Dunav/Dunav.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Dunav.dll"]
