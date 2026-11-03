# Alap image .NET-hez
FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Backend fájlok bemásolása
COPY . .

# Függőségek telepítése
RUN dotnet restore

# Projekt buildelése
RUN dotnet build

# Backend portja
EXPOSE 8000

# ASP.NET Core indítása
CMD ["dotnet", "run", "--urls", "http://0.0.0.0:8000"]
