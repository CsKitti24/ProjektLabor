# Egyetemi Kulcs- és Teremnyilvántartó Rendszer

Az alkalmazás célja az egyetemi teremfoglalások, a teremkulcsok kiadásának és visszavételének, valamint az ezekhez kapcsolódó eseményeknek a digitális kezelése.

## Technológiai stack

- **Frontend:** React + TypeScript + Vite
- **Backend:** ASP.NET Core Web API
- **Adatbázis:** Microsoft SQL Server
- **Adatelérés és migrációk:** Entity Framework Core
- **Futtatás:** Docker és Docker Compose
- **Verziókezelés és CI:** GitHub, GitHub Actions

## Rendszerarchitektúra

```mermaid
flowchart TD
    U[Felhasználó böngészője] --> FE[React + TypeScript frontend]
    FE -->|REST / HTTP| API[ASP.NET Core Web API]
    API -->|Entity Framework Core| DB[(Microsoft SQL Server)]
    CI[GitHub Actions CI] -. build és tesztek .-> FE
    CI -. build és tesztek .-> API
```

A frontend az API-n keresztül kommunikál a backenddel. Az adatbázis közvetlenül nem érhető el a böngészőből.

## Előfeltételek

- Git
- Docker Desktop vagy Docker Engine, működő Docker Compose támogatással
- A GitHub repositoryhoz való hozzáférés

A frontend és a backend helyi, konténeren kívüli fejlesztéséhez Node.js/npm, illetve a projektben használt .NET SDK is szükséges lehet. A Dockeres indításhoz ezeket nem feltétlenül kell külön a gazdagépre telepíteni.

## Környezeti változók

A repository gyökerében található `.env.example` fájlból készíts helyi `.env` fájlt, majd töltsd ki a szükséges értékeket:

```bash
cp .env.example .env
```

Windows PowerShell alatt:

```powershell
Copy-Item .env.example .env
```

A tényleges változóneveket és a kötelező értékeket a `.env.example` és a `docker-compose.yml` határozza meg. A `.env` fájlt ne töltsd fel a Git repositoryba, és valódi jelszót, tokent vagy egyéb titkot ne commitolj.

A Docker Compose az `ASPNETCORE_ENVIRONMENT` változó hiányában `Production` környezetben indítja a backendet. Töltsd ki a `.env` fájlban az egyedi `MSSQL_SA_PASSWORD`, `JWT_ISSUER`, `JWT_AUDIENCE` és `JWT_SIGNING_KEY` értékeket. A JWT-aláírókulcs legalább 32 bájt legyen; például `openssl rand -base64 48` paranccsal állíthatsz elő véletlen értéket. Éles környezetben hiányzó vagy rövid kulccsal az alkalmazás nem indul el.

Helyi fejlesztéshez `ASPNETCORE_ENVIRONMENT=Development` használható. Ebben a környezetben a [backend/appsettings.Development.json](./backend/appsettings.Development.json) tartalmaz egy kizárólag fejlesztéshez használható JWT-kulcsot; ezt az értéket éles környezetben tilos használni. Az `appsettings.json` nem tartalmaz aláírókulcsot.

### Első indítás: adminisztrátor inicializálása

Üres adatbázis esetén a migrációk után a backend egyetlen adminisztrátori fiókot hoz létre a `BOOTSTRAP_ADMIN_USERNAME` és `BOOTSTRAP_ADMIN_PASSWORD` változók alapján. A felhasználónév 3–50 ASCII betű, szám, pont, aláhúzás vagy kötőjel lehet; a jelszó legalább 12, legfeljebb 72 UTF-8 bájt legyen. A jelszó BCrypt hash-ként kerül mentésre. Hiányos vagy érvénytelen inicializáló adatokkal az üres adatbázisú alkalmazás egyértelmű hibával leáll.

Az inicializálás csak akkor történik meg, ha az adatbázisban még nincs felhasználó. Az első sikeres indulás után vedd ki a bootstrap felhasználónevet és jelszót a `.env` fájlból, és ne tárold őket forráskódban vagy más nem biztonságos helyen. Nincs nyilvános admin-létrehozó API. Megjegyzés: ebben a repository-verzióban nincs felhasználói bejelentkezési vagy felhasználókezelési HTTP-végpont; a bootstrap az admin fiókot készíti elő az alkalmazáson kívüli, biztonságos hitelesítési folyamathoz.

A JWT `sub` claimje az adatbázisbeli felhasználóazonosító, a `role` claim pedig az alábbi kanonikus értékek egyike legyen: `Oktato`, `Portas`, `Adminisztrator`, `UzemeltetesiIgazgato`.

A következő adatbázis-migráció a korábban eltárolt ékezetes szerepkörértékeket ezekre a kanonikus ASCII értékekre alakítja át; a fiókok egyéb adatait nem módosítja.

## Indítás Docker Compose segítségével

A repository gyökérkönyvtárában futtasd:

```bash
docker compose up --build -d
```

A szolgáltatások állapotának ellenőrzése:

```bash
docker compose ps
```

A naplók megtekintése:

```bash
docker compose logs -f
```

Egy szolgáltatás naplója külön is lekérhető, például:

```bash
docker compose logs -f backend
docker compose logs -f frontend
docker compose logs -f mssql
```

Leállítás:

```bash
docker compose down
```

A konténerek és az adatbázis tartós adatait tároló volume-ok törlése nem része a szokásos leállításnak. **Ne használd a `docker compose down -v` parancsot, ha meg akarod tartani az adatbázis adatait.**

## Szolgáltatások és portok

A jelenlegi fejlesztői Compose-beállításban rögzített portkiosztás:

| Szolgáltatás | Gazdagépen elérhető port | Konténerport | Megjegyzés |
|---|---:|---:|---|
| Frontend | `3000` | `3000` | Böngészőből: `http://localhost:3000` |
| Backend API | `5000` | `5000` | Az API gazdagépről: `http://localhost:5000` |
| Microsoft SQL Server | `1433` | `1433` | Adatbázis-kapcsolathoz; a hitelesítési adatokat a `.env` tartalmazza |

A portok a `docker-compose.yml` fájlban ellenőrizhetők. Ha ott változnak, ezt a táblázatot is frissíteni kell.

## API-ellenőrzés

A backend az alábbi ellenőrző és adatlekérdező végpontokat biztosítja:

### 1. Egészségellenőrzés (Health Check)

```text
GET http://localhost:5000/api/health
```

- **Sikeres válasz (HTTP 200 OK):**
```json
{
  "status": "ok",
  "database": "connected"
}
```
- **Hiba esetén (HTTP 503 Service Unavailable):**
```json
{
  "status": "unhealthy",
  "database": "disconnected"
}
```

### 2. Termek listázása (Rooms Query)

```text
GET http://localhost:5000/api/rooms
```

- **Válasz (HTTP 200 OK):** Termek listája DTO formátumban (üres lista, ha még nincs rögzített terem):
```json
[
  {
    "id": 1,
    "nev": "I. előadó",
    "kapacitas": 120,
    "leiras": "Főépület 1. emelet",
    "aktivalva": true
  }
]
```

### 3. Egy adott terem lekérdezése

```text
GET http://localhost:5000/api/rooms/{id}
```

- **Sikeres válasz (HTTP 200 OK):** A kiválasztott terem DTO adatai.
- **Ha nem található (HTTP 404 Not Found):** `{"message": "A(z) {id} azonosítójú terem nem található."}`

### Mesterkulcs-jogosultságok

A jogosultságok kezeléséhez hitelesített, `Adminisztrator` szerepkörű felhasználó szükséges. A POST-kérés JSON-törzse:

```json
{ "userId": 123 }
```

| Metódus | Útvonal | Művelet |
|---|---|---|
| `GET` | `/api/keys/{id}/master-access` | A mesterkulcshoz jogosult oktatók listázása |
| `POST` | `/api/keys/{id}/master-access` | Jogosultság adása egy oktatónak |
| `DELETE` | `/api/keys/{id}/master-access/{userId}` | Jogosultság visszavonása |

A végpontok csak `Mesterkulcs` típusú kulcsot fogadnak el; hozzárendeléskor a megadott felhasználónak `Oktato` szerepkörűnek kell lennie.

## Karbantartási idősávok API

- `GET /api/maintenance?teremId=1&startTime=2026-11-02T08:00:00Z&endTime=2026-11-02T18:00:00Z` – idősávok listázása; minden szűrő opcionális, az időtartomány az átfedő idősávokat adja vissza.
- `GET /api/maintenance/{id}` – idősáv részletei.
- `POST /api/maintenance` – új idősáv rögzítése.
- `PUT /api/maintenance/{id}` – idősáv módosítása.
- `DELETE /api/maintenance/{id}` – idősáv törlése.

A karbantartási listázás és részletezés hitelesítést igényel, a létrehozás, módosítás és törlés pedig kizárólag `Adminisztrator` jogosultságú, adatbázisban is megfelelő szerepkörrel rendelkező felhasználónak engedélyezett. A létrehozási és módosítási kérések törzse: `teremId`, `startTime`, `endTime`, valamint opcionálisan `leiras`. A foglalással ütköző idősáv `409 Conflict` választ eredményez; a megszüntetett foglalások nem blokkolják a karbantartást.

## Megbízhatósági pontozás API

- `GET /api/reliability/users/{userId}` – az oktató aktuális tanévi pontszáma (0–100), valamint az aktuális következmény.
- `GET /api/reliability/users/{userId}/events` – az oktató összes pontozási eseménye, beleértve a korábbi tanévek előzményeit.
- `GET /api/reliability/my` – a bejelentkezett oktató saját aktuális tanévi pontszáma.
- `POST /api/reliability/correction` – adminisztrátori pontkorrekció; törzse: `userId`, `pont` (nullától eltérő egész szám, negatív korrekció is lehetséges) és kötelező, legfeljebb 500 karakteres `indoklas`.

A pontszámot a backend az eseményekből, időrendi sorrendben számítja, és minden esemény után 0–100 közé korlátozza. A tanév szeptember 1-jén kezdődik; a korábbi tanévek eseményei és auditbejegyzései megmaradnak. A 10–19 pontos érték figyelmeztetést, a 20–29 pontos kötelező elbeszélgetést, a 30 vagy több pont elbeszélgetést és kulcskezelési/biztonsági tréninget jelent. Az oktató csak a saját adatait kérheti le, az adminisztrátor és az üzemeltetési igazgató más oktató adatait is megtekintheti. A korrekció csak adatbázisban is adminisztrátori szerepkörű felhasználónak engedélyezett; a pontozási esemény és az auditbejegyzés egy tranzakcióban kerül mentésre.

## Adatbázis és migrációk

Az adatbázis sémáját Entity Framework Core migrációk verziózzák (`backend/Migrations`).
A kezdeti migráció neve: `InitialCreate`.

- **Migrációk futtatása fejlesztői parancssorból:**
```bash
dotnet tool run dotnet-ef database update --project backend
```

- **Automatikus migráció konténer indulásakor:**
A `docker-compose.yml`-ben a `backend` konténer számára az `APPLY_MIGRATIONS=true` környezeti változó van beállítva, így a backend indításakor automatikusan és biztonságosan lefut a `Database.Migrate()` parancs.

## Tesztek és ellenőrzések futtatása

A fejlesztés során és Pull Request benyújtása előtt helyileg is lefuttathatók az ellenőrzések:

### Backend build és tesztek
```bash
dotnet build ProjektLabor.sln
dotnet test ProjektLabor.sln
```

### Frontend lint és build
```bash
cd frontend
npm ci
npm run lint
npm run build
```

### Docker konfiguráció és konténer-buildek ellenőrzése
```bash
docker compose --env-file .env.example config
docker compose --env-file .env.example build
```

## CI – GitHub Actions

A CI-folyamatnak Pull Request nyitásakor és frissítésekor, valamint a `main` ágra történő push/merge után kell lefutnia. Legalább az alábbi lépéseket kell végrehajtania:

1. Repository checkout.
2. Backend restore és build.
3. Backend unit tesztek futtatása.
4. Frontend függőségek telepítése és build.
5. Linter futtatása, amennyiben a projektben konfigurálva van.

A CI állapotát a Pull Requesten kell ellenőrizni. A sikeres helyi futás önmagában nem helyettesíti a GitHub Actions futás ellenőrzését.

## Fejlesztési konvenciók

A módosításokat külön brancheken készítsétek, majd Pull Requesten keresztül integráljátok a `main` ágba. A projektben használt commitprefixek:

- `feat:` – új funkció
- `fix:` – hibajavítás
- `refactor:` – belső átszervezés
- `test:` – tesztek hozzáadása vagy módosítása
- `docs:` – dokumentáció

A változtatás legyen kicsi, célzott, és a Pull Request írja le a módosítás célját, a tesztelés eredményét, illetve az esetleges ismert korlátokat.

## A rendszer tervezett fő moduljai

- Terem- és kulcsnyilvántartás
- Teremfoglalás 45 perces időszeletekkel és ütközésvizsgálattal
- Kulcskiadás és -visszavétel rögzítése
- Hibajegykezelés és karbantartási idősávok
- Felhasználói szerepkörök és jogosultságok (RBAC)
- Auditnapló
- Foglalás vége előtti értesítés
- Oktatói megbízhatósági pontozás
- Napi/heti riportok

Ezek közül a második bemutatóra az infrastruktúra, az első adatbázisséma, a backend–adatbázis kapcsolat, az egészségellenőrző végpont, egy adatlekérdező végpont és legalább egy CI-ban futó unit teszt a prioritás.

## Ismert korlátok / frissítendő adatok

A README-t a repository tényleges állapotával együtt kell karbantartani. Az API pontos adatlekérdező útvonalát, az alkalmazott .NET-verziót, az EF Core provider/verziót, a migrációk futtatásának pontos módját és a CI workflow fájlnevét csak akkor szabad konkrét értékként feltüntetni, ha azokat a kódbázisban ellenőriztük.
