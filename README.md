# Egyetemi Kulcs- és Teremnyilvántartó Rendszer

A projekt célja az egyetemi termek foglalásának, a teremkulcsok kiadásának és visszavételének, valamint az ezekhez kapcsolódó események digitális kezelése.

## Tervezett technológiák

* React
* TypeScript
* ASP.NET Core
* Microsoft SQL Server
* Entity Framework Core
* Docker
* Docker Compose

## Tervezett architektúra

```text
Browser
   |
   v
React Frontend
   |
   | REST API
   v
ASP.NET Core Backend
   |
   | Entity Framework Core
   v
Microsoft SQL Server
```

## Indítás

A projekt fejlesztői környezetének előkészítése az alábbi paranccsal indítható el (miután átmásoltad a `.env.example` fájlt `.env` néven, és beállítottad a jelszót):

```bash
docker compose up --build
```

**Figyelem:** Ez jelenleg az induló fejlesztői környezet előkészítése. A teljes alkalmazás funkcionalitása (adatbázis sémák, üzleti logika, frontend felületek) későbbi mérföldkövekben készül el.
