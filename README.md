# Egyetemi Kulcs- és Teremnyilvántartó Rendszer

Az alkalmazás egyetemi teremfoglalások, teremkulcsok, karbantartási idősávok és a kapcsolódó események digitális kezelésére szolgál.

## Technológiai stack

- **Frontend:** React, TypeScript, Vite
- **Backend:** ASP.NET Core Web API (.NET 8)
- **Adatbázis:** Microsoft SQL Server
- **Adatelérés és migrációk:** Entity Framework Core
- **Hitelesítés:** JWT, szerepkör-alapú hozzáférés-vezérlés (RBAC)
- **Futtatás:** Docker Compose

## Előfeltételek és konfiguráció

Docker Desktop vagy Docker Engine Docker Compose támogatással szükséges. Helyi, konténer nélküli fejlesztéshez Node.js/npm és .NET 8 SDK is kellhet.

Készíts helyi `.env` fájlt a példából:

```bash
cp .env.example .env
```

PowerShell alatt:

```powershell
Copy-Item .env.example .env
```

A Docker indítása előtt cseréld le az `.env` fájlban az `MSSQL_SA_PASSWORD` helyőrző értékét egyedi, az MSSQL jelszókövetelményeinek megfelelő jelszóra. A fájl a `JWT_SIGNING_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE` és opcionálisan a `JWT_EXPIRES_IN_HOURS` értékeit is beállíthatja. A példában szereplő JWT-kulcs csak helyi fejlesztésre való; éles környezetben adj meg legalább 32 bájtos, titkos kulcsot, és ne commitold a `.env` fájlt vagy valódi hitelesítő adatot.

## Indítás Docker Compose segítségével

A repository gyökerében:

```bash
docker compose up --build -d
docker compose ps
```

Naplók:

```bash
docker compose logs -f
docker compose logs -f backend
docker compose logs -f frontend
docker compose logs -f mssql
```

Leállítás:

```bash
docker compose down
```

A normál leállítás megtartja az adatbázis volume-ját. Ne használd a `docker compose down -v` parancsot, ha meg szeretnéd őrizni az adatokat.

| Szolgáltatás | Gazdagép | Konténer | Elérés |
|---|---:|---:|---|
| Frontend | `3000` | `3000` | `http://localhost:3000` |
| Backend API | `5000` | `5000` | `http://localhost:5000` |
| MSSQL | `1433` | `1433` | SQL-kliensből, a `.env` hitelesítő adataival |

A frontend a backend egészségellenőrzésének sikeréig nem indul el; a backend pedig megvárja az adatbázis egészséges állapotát. A backend konténer az `APPLY_MIGRATIONS=true` beállítással indul, és alkalmazza a függő EF Core migrációkat.

## API és hitelesítés

Fejlesztési környezetben a Swagger UI címe `http://localhost:5000/swagger`; az OpenAPI leírás a `http://localhost:5000/swagger/v1/swagger.json` útvonalon érhető el. A Swagger felületen a bejelentkezés után a JWT `Authorize` funkcióval használható.

- `GET /api/health` – adatbázis-kapcsolatot is ellenőrző health check; nem igényel hitelesítést.
- `POST /api/auth/login` – felhasználónév és jelszó alapján JWT-t ad vissza.
- `GET /api/auth/me`, `POST /api/auth/logout` – a hitelesített felhasználó adatai, illetve kliensoldali kijelentkezés.
- `/api/users`, `/api/rooms`, `/api/keys`, `/api/key-movements`, `/api/bookings`, `/api/tickets` – felhasználó-, terem-, kulcs-, kulcsmozgás-, foglalás- és hibajegy-végpontok; a részletes jogosultságokat az endpointok RBAC-szabályai határozzák meg.
- `/api/maintenance` – karbantartási idősávok listázása és kezelése.
- `/api/keys/{id}/master-access` – mesterkulcs-hozzáférések kezelése.
- `/api/reliability` – oktatói megbízhatósági pontszámok és események.
- `/api/parameters` – rendszerparaméterek lekérdezése és adminisztrátori módosítása.
- `/api/audit` – adminisztrátori auditnapló-lekérdezés és szűrés.
- `/api/reports` – napi, heti, teremkihasználtsági, kulcsforgalmi és megbízhatósági riportok.
- `/weatherforecast` – a `main` ágról megőrzött, nyilvános ASP.NET minta-végpont.

A karbantartási, mesterkulcs-, rendszerparaméter-, audit-, riport- és megbízhatósági műveletek hitelesített JWT-t igényelnek; a szerepkör-korlátozott műveleteket a backend ellenőrzi. Az értesítési entitás szerepel az adatmodellben, de az értesítési végpontok és szolgáltatás még nincsenek implementálva. Az egyes végpontok és DTO-k részletei a Swagger dokumentációban és a `docs/API_DESIGN.md` fájlban találhatók.

### Karbantartás, mesterkulcsok és megbízhatóság

- Karbantartás: `GET /api/maintenance` (opcionális terem- és időtartomány-szűrőkkel), `GET /api/maintenance/{id}`, valamint `POST`, `PUT` és `DELETE` műveletek. Az írási műveletek adminisztrátori jogosultságot igényelnek; foglalással ütköző idősáv `409 Conflict` választ ad.
- Mesterkulcs: `GET` és `POST /api/keys/{id}/master-access`, illetve `DELETE /api/keys/{id}/master-access/{userId}`. A jogosultságkezelést adminisztrátor végezheti; csak mesterkulcshoz adható hozzáférés.
- Megbízhatóság: `GET /api/reliability/users/{userId}`, `GET /api/reliability/users/{userId}/events`, `GET /api/reliability/my`, és adminisztrátori `POST /api/reliability/correction`. A pontszám eseményekből számítódik, és 0–100 közé korlátozott.

Példa health check és teremlekérdezés:

```http
GET http://localhost:5000/api/health
GET http://localhost:5000/api/rooms
```

A terem-végpontok hitelesítést igényelnek; a token a `POST /api/auth/login` válaszából szerezhető be.

## Adatbázis, build és tesztek

Az adatbázis sémáját az `backend/Migrations` alatti EF Core migrációk verziózzák. Helyi migrációfuttatás:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project backend
```

Backend build és tesztek:

```bash
dotnet build ProjektLabor.sln
dotnet test ProjektLabor.sln
```

Frontend lint és build:

```bash
cd frontend
npm ci
npm run lint
npm run build
```

Compose konfiguráció ellenőrzése:

```bash
docker compose --env-file .env.example config
```

## CI és fejlesztési folyamat

A GitHub Actions ellenőrzi a backend restore/build és teszt lépéseit, valamint a frontend telepítését, lintelését és buildjét. Pull Request nyitásakor és frissítésekor ellenőrizd a CI eredményét is; a sikeres helyi futás nem helyettesíti a GitHub Actions ellenőrzését.

A fő tervezett modulok a terem- és kulcsnyilvántartás, 45 perces időszeletekkel működő foglalás és ütközésvizsgálat, kulcskiadás és -visszavétel, hibajegyek, RBAC, auditnapló, értesítések, megbízhatósági pontozás és riportok. A működő végpontok és az aktuális implementáció a Swagger dokumentációban, illetve a `docs/API_DESIGN.md` fájlban található.

## Fejlesztési konvenciók

A módosításokat külön brancheken készítsétek, és Pull Requesten keresztül integráljátok. Használt commitprefixek: `feat:`, `fix:`, `refactor:`, `test:` és `docs:`. A Pull Request írja le a módosítás célját, az ellenőrzések eredményét és az ismert korlátokat.
