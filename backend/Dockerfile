# --- Build: forrás fordítása és publikálása Release módban ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend.csproj ./
RUN dotnet restore backend.csproj

COPY . .
RUN dotnet publish backend.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# --- Futtatás: csak a runtime és a publikált alkalmazás, SDK nélkül ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# A curl a docker-compose healthcheckhez kell.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8000
EXPOSE 8000

# Nem root felhasználóként fut (az aspnet image beépített "app" felhasználója).
USER app

ENTRYPOINT ["dotnet", "backend.dll"]
