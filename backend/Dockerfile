# Alap image .NET-hez
FROM mcr.microsoft.com/dotnet/sdk:8.0

WORKDIR /app

# Backend fájlok bemásolása
COPY . .

# Függőségek telepítése
RUN dotnet restore

# Projekt buildelése
RUN dotnet build

# Backend portja
EXPOSE 80

# ASP.NET Core indítása
CMD ["dotnet", "run", "--urls", "http://0.0.0.0:80"]