# Egyetemi kulcs- és teremnyilvántartó rendszer

Az alkalmazás egyetemi termek, foglalások, kulcsok és kulcsmozgások központi
nyilvántartására készül. A backend jelenlegi állapota a fő üzleti modulokat,
JWT-alapú hitelesítést, szerepkörös jogosultságkezelést, SQL Server-adatbázist,
riportokat és automatizált teszteket tartalmaz. A frontend még a Vite kezdő
felülete, ezért a rendszer jelenleg backend-központú, integrációs állapotban van.

> **Állapotfelvétel:** 2026. november 2.
> A „kész” jelölés a repositoryban ténylegesen megtalálható és tesztelt
> funkcionalitást jelenti, nem csak a tervben szereplő célt.

## Technológiai stack

| Réteg | Technológia | Repository-állapot |
|---|---|---|
| Frontend | React 19, TypeScript, Vite 8 | Kezdő Vite-felület, a domain UI még hiányzik |
| Backend | ASP.NET Core Web API, .NET 8 | Kész, endpoint-modulokra bontva |
| Adatbázis | Microsoft SQL Server 2022 | Docker Compose-ban konfigurálva |
| ORM/migráció | Entity Framework Core 8, SQL Server provider | Kész, 4 migráció verziózva |
| Hitelesítés | JWT Bearer, BCrypt.Net-Next | Kész |
| Dokumentáció | Swagger/OpenAPI v1 | Development környezetben elérhető |
| Tesztelés | xUnit, ASP.NET Core test host, EF InMemory | Kész; külön SQL Server teszt is előkészítve |
| CI/CD | GitHub Actions | Backend, frontend és Docker ellenőrzés konfigurálva |

## Architektúra

```mermaid
flowchart LR
    Browser["Böngésző"] --> Frontend["React + TypeScript + Vite<br/>localhost:3000"]
    Frontend -->|REST / JSON + Bearer JWT| API["ASP.NET Core Web API<br/>localhost:8000"]
    API --> Auth["JWT validáció<br/>RBAC policy-k"]
    API --> EF["Entity Framework Core 8"]
    EF --> DB[("SQL Server 2022<br/>KeyAndRoomDb")]
    API --> Swagger["Swagger / OpenAPI<br/>Development"]
    CI["GitHub Actions"] -.-> API
    CI -.-> Frontend
```

Docker Compose három szolgáltatást indít közös bridge hálózaton:

```mermaid
flowchart TB
    FE["frontend :3000"] --> BE["backend :8000"]
    BE --> SQL[("mssql :1433")]
    BE -. "startup: APPLY_MIGRATIONS=true" .-> SQL
    SQL --> Volume[("mssql-data volume")]
```

## Elkészült backend

### Hitelesítés és rendszer

- `POST /api/auth/login` – felhasználónév/jelszó ellenőrzése és JWT kiadása.
- `POST /api/auth/logout` – kliensoldali token-törléshez visszajelzés.
- `GET /api/auth/me` – aktuális felhasználó adatai.
- `GET /api/health` – API- és adatbázis-kapcsolat ellenőrzése.
- JWT issuer, audience, élettartam és legalább 32 bájtos aláírókulcs validálása.
- Üres adatbázisnál opcionális, egyszeri admin bootstrap BCrypt jelszóhash-sel.
- `Adminisztrator`, `Portas`, `Oktato`, `UzemeltetesiIgazgato` szerepkörök és
  ezekre épülő policy-k.

### Üzleti végpontok

| Modul | Végpontok | Állapot |
|---|---|---|
| Felhasználók | `/api/users` | Kész, adminisztrátori CRUD és szerepkörmódosítás |
| Termek | `/api/rooms` | Kész, CRUD, elérhetőség és teremhez tartozó kulcsok |
| Kulcsok | `/api/keys` | Kész, CRUD, státusz és mozgáslista |
| Mesterkulcs-jogosultság | `/api/keys/{id}/master-access` | Kész, admin grant/revoke és egyediségvédelem |
| Foglalások | `/api/bookings` | Kész, saját/listázás, CRUD, átfedés- és karbantartás-ellenőrzés |
| Kulcsmozgások | `/api/key-movements` | Kész, kiadás, visszavétel, lista, részlet, overdue |
| Hibajegyek | `/api/tickets` | Kész, létrehozás, listázás, részlet és státuszkezelés |
| Karbantartás | `/api/maintenance` | Kész, admin CRUD és foglalásütközés-védelem |
| Megbízhatóság | `/api/reliability` | Kész, pontszámok, események és admin korrekció |
| Rendszeradminisztráció | `/api/parameters`, `/api/audit` | Kész, paraméterek és auditnapló lekérdezése/módosítása |
| Riportok | `/api/reports` | Kész, napi/heti, teremkihasználtsági, kulcsforgalmi és megbízhatósági riport |

Az összes végpont részletes kérés-/válaszformátuma és jogosultsága a
[docs/API_DESIGN.md](./docs/API_DESIGN.md) fájlban található. Development
környezetben a Swagger UI a `http://localhost:8000/swagger` címen érhető el.

### Adatmodell és integritás

```mermaid
erDiagram
    Felhasznalo ||--o{ Foglalas : letrehoz
    Terem ||--o{ Foglalas : tartozik
    Terem ||--o{ Kulcs : hasznal
    Kulcs ||--o{ Kulcsmozgas : mozgas
    Foglalas ||--o{ Kulcsmozgas : kapcsolodik
    Kulcs ||--o{ MesterkulcsJogosultsag : jogosultsag
    Felhasznalo ||--o{ MesterkulcsJogosultsag : kap
    Terem ||--o{ KarbantartasiIdosav : zarva
    Felhasznalo ||--o{ MegbizhatosagiEsemeny : pontoz
    Felhasznalo ||--o{ AuditLog : vegrehajt
```

Az EF Core modell jelenleg 13 entitást tartalmaz: felhasználó, terem, kulcs,
kulcs–terem kapcsolat, mesterkulcs-jogosultság, foglalás, kulcsmozgás,
karbantartási idősáv, hibajegy, értesítés, megbízhatósági esemény, auditnapló
és rendszerparaméter. A `SaveChanges` foglalás-átfedést és karbantartási
ütközést ellenőriz; a felhasználónév, e-mail és mesterkulcs-hozzáférés egyedi.

## Üzleti szabályok, amelyek már a backendben vannak

- A foglalási idők egy napon belül, 08:00–20:00 között, 45 perces rácson
  és 45 perces egységekben validálódnak.
- Aktív foglalások nem fedhetik egymást; `Megszuntetve` és `NoShow` nem blokkol.
- Karbantartási idősávval átfedő foglalás nem menthető.
- A megbízhatósági események az aktuális tanévben 0–100 közé korlátozott
  pontszámot adnak, az előzmények és auditbejegyzések megmaradnak.
- A szerepkörök és a védett műveletek backend oldalon is ellenőrzöttek.
- A rendszerparaméterek és adminisztrátori korrekciók auditálódnak.

## Mi hiányzik vagy nincs még teljesen bekötve?

| Tétel | Jelenlegi állapot | Következő munka |
|---|---|---|
| Frontend domainfelület | Vite starter képernyő látható | Login, szerepkörös navigáció és üzleti oldalak elkészítése |
| Foglalás vége előtti értesítés | `Ertesites` entitás létezik, de nincs generáló worker/endpoint | Background service, küldési állapot és frontend megjelenítés |
| Automatikus no-show feldolgozás | `NoShow` státusz és paraméter létezik, időzített feldolgozó nincs | Ütemezett job, idempotens státuszváltás és tesztek |
| Értesítési csatorna | Nincs e-mail/push/in-app kézbesítő | Kézbesítési stratégia és konfiguráció |
| Production üzemeltetés | Compose fejlesztési/teszt futtatásra alkalmas | HTTPS, szűkített CORS, titokkezelés, monitoring és backup |
| Swagger elérhetőség | Csak Development környezetben | Tudatos production policy és publikált API-specifikáció |
| `/weatherforecast` | Template maradvány végpont | Törlés a backend takarításakor |

## Indítás

### Előfeltételek és konfiguráció

- .NET 8 SDK helyi backend- és tesztfuttatáshoz.
- Docker Desktop Compose támogatással a teljes stackhez.
- SQL Server 2022 egy külön, eldobható adatbázissal az integrációs tesztekhez.

Hozd létre a helyi konfigurációt, majd töltsd ki a JWT- és SQL Server-értékeket:

```bash
cp .env.example .env
```

A `MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY`, `JWT_ISSUER`, `JWT_AUDIENCE`,
`BOOTSTRAP_ADMIN_USERNAME` és `BOOTSTRAP_ADMIN_PASSWORD` kötelező
Compose-változók. A bootstrap-admin értékek meglévő adatbázisnál nem kerülnek
felhasználásra; a jelszó legalább 12 karakteres legyen.

### Docker Compose

```bash
docker compose up --build -d
docker compose ps
curl http://localhost:8000/api/health
```

Leállítás:

```bash
docker compose down
```

A `mssql-data` volume megőrzéséhez ne használd a `docker compose down -v`
parancsot. A kötelező és opcionális környezeti változókat az
[.env.example](./.env.example) és a [docker-compose.yml](./docker-compose.yml)
határozza meg.

A backend az `mssql` health check után indul, és `APPLY_MIGRATIONS=true` mellett
automatikusan alkalmazza a teljes EF Core migrációs láncot. Apple Siliconon a
SQL Server hivatalos image-e jelenleg csak `linux/amd64` image-ként használható,
ezért a Compose Docker-emulációt kér; ez nem natív ARM64 támogatás.

### Migrációk és tesztfelhasználó

```bash
dotnet ef migrations list --project backend/backend.csproj
dotnet ef migrations script --project backend/backend.csproj --idempotent --output /tmp/projektlabor-migrations.sql
```

Az adatbázist Compose indításakor a backend inicializálja. Az első admin a `BOOTSTRAP_ADMIN_USERNAME` és
`BOOTSTRAP_ADMIN_PASSWORD` megadásával jön létre. A négy prezentációs
demófelhasználó jelszava a `DEMO_*_PASSWORD` változókból érkezik; a seedelés
felhasználónévenként idempotens, és meglévő felhasználót nem módosít.

### Helyi demófelhasználók

Az alábbi fiókok kizárólag helyi fejlesztési és prezentációs célra szolgálnak:

| Felhasználónév | Szerepkör | Jelszó |
|---|---|---|
| `demo-oktato` | Oktato | `DemoOktato2026!` |
| `demo-portas` | Portas | `DemoPortas2026!` |
| `demo-admin` | Adminisztrator | `DemoAdmin2026!` |
| `demo-igazgato` | UzemeltetesiIgazgato | `DemoIgazgato2026!` |

Ezeket a `DEMO_OKTATO_PASSWORD`, `DEMO_PORTAS_PASSWORD`,
`DEMO_ADMIN_PASSWORD` és `DEMO_IGAZGATO_PASSWORD` környezeti változók adják
át a backendnek. A jelszavak csak BCrypt hash formában kerülnek az adatbázisba.
Bejelentkezéskor a backend JWT tokent ad vissza, amelyben a szerepkör claim
alapján működnek a jogosultsági policy-k. Token nélkül a védett végpontok `401`,
nem megfelelő szerepkörrel pedig `403` választ adnak.

### Helyi fejlesztés

```bash
dotnet build ProjektLabor.sln --configuration Release
dotnet test ProjektLabor.sln --configuration Release \
  --filter "FullyQualifiedName!~SqlServerIntegrationTests"

cd frontend
npm ci
npm run lint
npm run build
```

SQL Server-integrációs tesztekhez adj meg egy külön, eldobható adatbázist:

```bash
SQLSERVER_TEST_CONNECTION='Server=localhost,1433;Database=ProjektLaborSqlIntegration;User Id=sa;Password=<teszt-jelszó>;TrustServerCertificate=True;Encrypt=False' \
  dotnet test backend.Tests/backend.Tests.csproj --configuration Release \
  --filter "FullyQualifiedName~SqlServerIntegrationTests"
```

A tesztek migrálnak, adatot írnak/olvasnak, ellenőrzik a foreign key és unique
korlátozásokat, valamint a tranzakció-visszagörgetést. Ne használd a fejlesztői
vagy éles adatbázist.

## CI

A [CI workflow](./.github/workflows/ci.yml) `main` push és pull request esetén:

1. restore/build/test futtat a backendre,
2. külön, eldobható SQL Server szolgáltatáson futtatja az integrációs teszteket,
3. lintet és buildet futtat a frontendben,
4. validálja a Compose konfigurációt és megkísérli a Docker buildet.

## Kapcsolódó dokumentumok

- [Projektterv és megvalósítási állapot](./projektterv.md)
- [API-tervezési dokumentáció](./docs/API_DESIGN.md)
- [Adatbázis- és migrációs megjegyzések](./database/README.md)
