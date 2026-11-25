# Egyetemi Kulcs- és Teremnyilvántartó Rendszer (ProjektLabor)

Egyetemi termek, teremfoglalások, fizikai kulcsok és kulcsmozgások teljes körű, szerepkör-alapú nyilvántartó és menedzsment rendszere.

---

## Főbb funkciók és szerepkörök

A rendszer négy jól elkülönülő szerepkört támogat, mindegyikhez dedikált jogosultságokkal és felülettel:

| Szerepkör | Fő felelősségi kör | Elérhető funkciók |
| :--- | :--- | :--- |
| **`Oktato`** | Foglalások és kulcshasználat | Teremkatalógus böngészése, új foglalás leadása, saját foglalások, megbízhatósági hibapont és rendszerbeli értesítések megtekintése, hibajegy bejelentése. |
| **`Portas`** | Kulcsok fizikai kiadása és visszavétele | Kulcsok kiadása/visszavétele az átvevő PIN-jével (a kiadás magától az esedékes foglaláshoz kötődik), kint lévő és lejárt kulcsok, kulcsmozgások naplója, esti kulcsvisszavételi értesítések, hibajegy rögzítése. |
| **`Adminisztrator`** | Rendszertörzsadatok és konfiguráció | Felhasználók kezelése (inaktiválás/újraaktiválás, szerepkör, PIN), termek és kulcsok kezelése, mesterkulcs-jogosultságok, karbantartási idősávok, hibajegy-státuszok, rendszerparaméterek, megbízhatósági korrekció, auditnapló. |
| **`UzemeltetesiIgazgato`** | Ellenőrzés és riportálás | Teremkihasználtsági statisztikák, napi és heti riportok, kulcsforgalom elemzése, oktatói megbízhatósági audit, teljes rendszeresemény-napló. |

---

## Technológiai stack

| Réteg | Technológia | Megjegyzés |
| :--- | :--- | :--- |
| **Frontend** | React 19, TypeScript, Vite, saját CSS, Lucide React | Szerepkörös SPA; élesben nginx szolgálja ki, és az `/api` kéréseket a backendre továbbítja. |
| **Backend** | ASP.NET Core 10 Minimal API, C# | Erősen típusos, moduláris végpontok, BCrypt jelszó- és kulcsátvételi PIN-kezelés, beépített RBAC házirendek. |
| **Adatbázis** | Microsoft SQL Server 2022 | Konténerizált relációs adatbázis tranzakció- és zárkezeléssel. |
| **ORM / Migráció** | Entity Framework Core 8 | Code-first megközelítés, automatikus séma-migrációk indításkor. |
| **Hitelesítés** | JSON Web Token HttpOnly sütiben + BCrypt | A token JavaScriptből nem olvasható; minden kérésnél adatbázis-ellenőrzés (aktív felhasználó, egyező szerepkör), bejelentkezési kéréskorlát, PIN-zárolás. |
| **Dokumentáció** | Swagger / OpenAPI v1 | Fejlesztői módban interaktív tesztelő felület (`/swagger`). |
| **Tesztelés & CI** | xUnit, WebApplicationFactory, Vitest, GitHub Actions | 198 backend teszt (valódi SQL Serveren is) és 69 frontend teszt, linting, Docker-build ellenőrzés. |

---

## Rendszerarchitektúra

```mermaid
flowchart LR
    Browser["Böngésző / Felhasználó"] --> Frontend["React + Vite Kliens<br/>:3000"]
    Frontend -->|"/api – REST / JSON + HttpOnly JWT-süti (nginx proxy)"| API["ASP.NET Core Web API<br/>:8000 (csak helyben)"]
    API --> Auth["Hitelesítés & RBAC Policy-k"]
    API --> Services["Háttérszolgáltatások<br/>(NoShow, foglaláslezárás, napi kulcszárás, értesítések)"]
    API --> EF["Entity Framework Core 8"]
    EF --> DB[("Microsoft SQL Server 2022<br/>:1433 (KeyAndRoomDb)")]
    DB --> Volume[("mssql-data perzisztens volume")]
```

---

## Adatbázis ER-modell

```mermaid
erDiagram

    FELHASZNALOK {
        int Id PK
        string Username UK
        string Email UK
        string JelszoHash
        string PinHash
        string Nev
        string Role
        boolean Aktiv
        int PinHibasProbalkozasok
        datetime PinZarolvaEddig
        datetime CreatedAt
        datetime UpdatedAt
    }

    TERMEK {
        int Id PK
        string Nev
        int Kapacitas
        string Leiras
        string Felszereltseg
        boolean Aktivalva
    }

    KULCSOK {
        int Id PK
        int TeremId FK
        string Szam UK
        string Tipus
        boolean Aktivalva
    }

    KULCSTEREM {
        int KulcsId PK, FK
        int TeremId PK, FK
    }

    MESTERKULCS_JOGOSULTSAGOK {
        int Id PK
        int KulcsId FK
        int FelhasznaloId FK
    }

    FOGLALASOK {
        int Id PK
        int FelhasznaloId FK
        int TeremId FK
        datetime StartTime
        datetime EndTime
        string Status
        string SpecialisIgenyek
    }

    KULCSMOZGASOK {
        int Id PK
        int KulcsId FK
        int? FoglalasId FK
        int? AtvevoFelhasznaloId FK
        int? TeremId "a kiadás terme"
        int OperatorId FK
        string MozgasTipus
        datetime Idopont
        string Megjegyzes
        string TeremAllapot
        string SerulesLeiras
    }

    KARBANTARTASI_IDOSAVOK {
        int Id PK
        int TeremId FK
        int FelhasznaloId FK
        datetime StartTime
        datetime EndTime
        string Leiras
    }

    HIBAJEGYEK {
        int Id PK
        int TeremId FK
        int FelhasznaloId FK
        string Kategoria
        string Leiras
        string Statusz
    }

    MEGBIZHATOSAGI_ESEMENYEK {
        int Id PK
        int FelhasznaloId FK
        int KulcsId FK
        int FoglalasId FK
        int KiadasiKulcsmozgasId FK
        int LetrehozoFelhasznaloId FK
        string EsemenyTipus
        int Pont
        boolean Automatikus
        datetime EredetiHatarido
        datetime TenylegesLeadasIdeje
    }

    AUDIT_LOGOK {
        int Id PK
        int FelhasznaloId FK
        string MuveletTipus
        string EntitasTipus
        string EntitasId
        string AutomationKey
        string RegiErtek
        string UjErtek
        string Indoklas
        datetime LetrehozasIdeje
    }

    ERTESITESEK {
        int Id PK
        int FoglalasId FK "nullable"
        int CimzettFelhasznaloId FK "nullable"
        int KulcsmozgasId FK "nullable"
        string ErtesitesTipus
        datetime IdoPont
        datetime Hatarido "nullable"
        string Allapot
        boolean Olvasva
        string AutomationKey
        datetime CreatedAt
    }

    RENDSZERPARAMETEREK {
        string Kulcs PK
        string Ertek
        string Leiras
        datetime ModositasIdeje
    }

    %% Felhasznalok es teremfoglalasok
    FELHASZNALOK ||--o{ FOGLALASOK : "letrehozza"
    TERMEK ||--o{ FOGLALASOK : "helyszine"

    %% Termek es kulcsok
    TERMEK ||--o{ KULCSOK : "rendelkezik"

    %% Mesterkulcsok es az altaluk nyithato termek
    KULCSOK ||--o{ KULCSTEREM : "hozzarendeles"
    TERMEK ||--o{ KULCSTEREM : "megnyithato"

    %% Mesterkulcs felveteli jogosultsagok
    KULCSOK ||--o{ MESTERKULCS_JOGOSULTSAGOK : "jogosultsag"
    FELHASZNALOK ||--o{ MESTERKULCS_JOGOSULTSAGOK : "rendelkezik"

    %% Fizikai kulcsmozgások
    KULCSOK ||--o{ KULCSMOZGASOK : "mozgasnaplo"
    FOGLALASOK o|--o{ KULCSMOZGASOK : "kapcsolodo foglalas"
    FELHASZNALOK o|--o{ KULCSMOZGASOK : "atvevo"
    FELHASZNALOK ||--o{ KULCSMOZGASOK : "operator"

    %% Karbantartasi idosavok
    TERMEK ||--o{ KARBANTARTASI_IDOSAVOK : "karbantartas"
    FELHASZNALOK ||--o{ KARBANTARTASI_IDOSAVOK : "rogziti"

    %% Hibajegyek
    TERMEK ||--o{ HIBAJEGYEK : "hibas terem"
    FELHASZNALOK ||--o{ HIBAJEGYEK : "bejelenti"

    %% Megbizhatosagi esemenyek
    FELHASZNALOK ||--o{ MEGBIZHATOSAGI_ESEMENYEK : "erintett felhasznalo"
    KULCSOK o|--o{ MEGBIZHATOSAGI_ESEMENYEK : "erintett kulcs"
    FOGLALASOK o|--o{ MEGBIZHATOSAGI_ESEMENYEK : "kapcsolodo foglalas"
    KULCSMOZGASOK o|--o{ MEGBIZHATOSAGI_ESEMENYEK : "kiadasi mozgas"
    FELHASZNALOK ||--o{ MEGBIZHATOSAGI_ESEMENYEK : "letrehozo"

    %% Audit naplozas
    FELHASZNALOK o|--o{ AUDIT_LOGOK : "muveletet vegzi"
    
    %% Felhasznaloi ertesitesek
    FOGLALASOK o|--o{ ERTESITESEK : "foglalasi ertesites"
    FELHASZNALOK o|--o{ ERTESITESEK : "cimzett"
    KULCSMOZGASOK o|--o{ ERTESITESEK : "kulcsvisszaveteli esemeny"
```

---

## Gyorsindítás (Docker Compose)

### 1. Környezeti változók előkészítése
Másold le a mellékelt minta-fájlt, és állíts be saját, biztonságos jelszavakat:
```bash
cp .env.example .env
```
*(Windows PowerShell esetén: `Copy-Item .env.example .env`)*

A `.env` fájlban add meg a kulcsokat:
- `MSSQL_SA_PASSWORD`: Erős adatbázis rendszergazda jelszó. Csak az SQL Server és az egyszeri `db-init` lépés használja.
- `MSSQL_APP_PASSWORD`: Az alkalmazás saját adatbázis-felhasználójának (`projektlabor_app`) jelszava (legalább 12 karakter, idézőjel nélkül). A `db-init` szolgáltatás induláskor létrehozza ezt a felhasználót, amely csak a `KeyAndRoomDb` adatbázishoz fér hozzá; a backend ezzel csatlakozik, nem az `sa` fiókkal. **Meglévő telepítésnél is fel kell venni a `.env`-be**, különben a `docker compose` nem indul.
- `AUTH_COOKIE_SECURE`: `true`, ha az alkalmazás HTTPS-t lezáró proxy mögött fut (lásd lent). Alapértelmezés: `false`.
- `JWT_SIGNING_KEY`: Legalább 32 bájtos véletlenszerű kulcs (pl. `openssl rand -base64 48`).
- `BOOTSTRAP_ADMIN_USERNAME` és `BOOTSTRAP_ADMIN_PASSWORD`: Az induló adminisztrátori fiók adatai (legalább 12 karakteres jelszó; a `REPLACE_WITH_` mintaértékeket a rendszer elutasítja). Akkor jön létre, ha nincs aktív adminisztrátor.
- `SAMPLE_ACCOUNTS_ENABLED`: `true` esetén a lenti mintafiókok aktívak maradnak (csak bemutatóhoz/fejlesztéshez). Alapértelmezés: `false`.
- `ALLOWED_HOSTS`: azok a gépnevek/IP-címek (pontosvesszővel), amelyeken a felhasználók a rendszert a böngészőből elérik, pl. `kulcs.iskola.hu;localhost;127.0.0.1;backend`. Más `Host` fejléccel érkező kérést a backend `400`-zal elutasít. Alapértelmezés: `localhost;127.0.0.1;backend` — **ha a rendszert más gép a szerver nevén vagy IP-címén éri el, ezt fel kell venni.**
- `BACKUP_DIR`, `BACKUP_INTERVAL_HOURS`, `BACKUP_RETENTION_DAYS`: az ütemezett adatbázismentés helye és gyakorisága (lásd lent). Alapértelmezés: `./backups`, 24 óra, 14 nap.
- *(Opcionális)* `CORS_ALLOWED_ORIGINS`: vesszővel elválasztott originök, ha a frontendet nem a beépített proxyn keresztül éred el.

#### Élesítés előtti ellenőrzőlista
1. Minden `REPLACE_WITH_` érték lecserélve; a `MSSQL_SA_PASSWORD`, a `MSSQL_APP_PASSWORD` és a `JWT_SIGNING_KEY` egyedi, véletlen érték (pl. `openssl rand -base64 48`), máshol nem használt.
2. `MSSQL_APP_PASSWORD` szerepel a `.env`-ben (régebbi telepítésnél is pótolni kell; a `db-init` ilyenkor a meglévő `projektlabor_app` felhasználó jelszavát is átírja).
3. `SAMPLE_ACCOUNTS_ENABLED=false`: a nyilvános jelszavú mintafiókok tiltva, belépés a `BOOTSTRAP_ADMIN_*` fiókkal. Bemutató után mindig állítsd vissza `false`-ra.
4. `ALLOWED_HOSTS` tartalmazza a nyilvános gépnevet.
5. HTTPS-proxy beállítva (lásd lent), `AUTH_COOKIE_SECURE=true`.
6. Az első mentés elkészült a `BACKUP_DIR` mappában, és lefutott egy helyreállítási próba.
7. A `.env` fájl jogosultsága szűkítve (`chmod 600 .env`), és nem kerül verziókezelésbe.

### Közös kezdő felhasználók

A migrációk minden adatbázisba felveszik az alábbi fiókokat és egy bemutató adatkészletet (`SeedRoleUsersAndReliabilitySample`: `B-101`/`B-102` mintaterem, `MINTA-B101`/`MINTA-B102` kulcs, Rádli Richárd 34 késve visszahozott kulcsa). Ha a mintafiókok nincsenek engedélyezve, a backend induláskor a bemutató adatkészletet is törli, feltéve hogy a mintakulcsokat azóta senki nem használta valódi kiadásra. Mivel a jelszavuk itt nyilvános, **a backend induláskor letiltja azokat, amelyek jelszava még az eredeti**, kivéve ha `SAMPLE_ACCOUNTS_ENABLED=true` (Docker) vagy `SampleAccounts:Enabled=true` (a `Development` környezet `appsettings.Development.json`-ja ezt bekapcsolja). Ha emiatt nem marad aktív adminisztrátor, a `BOOTSTRAP_ADMIN_*` adataiból jön létre egy (az azonos nevű, letiltott mintafiók átvételével).

| Felhasználónév | Jelszó | Szerepkör |
| :--- | :--- | :--- |
| `admin` | `Adminjelszo1!` | Adminisztrátor |
| `oktato` | `Oktatojelszo1!` | Oktató |
| `portas` | `Portasjelszo1!` | Portás |
| `igazgato` | `Igazgatojelszo1!` | Üzemeltetési igazgató |

Az `oktato` fiók kulcsátvételi PIN-je `4729`.
Bemutatóhoz a `.env`-ben állítsd be: `SAMPLE_ACCOUNTS_ENABLED=true`. Éles üzemben hagyd `false`-on. A saját jelszavát minden felhasználó a **Profil → Jelszó módosítása** űrlapon cserélheti le (ilyenkor a más eszközökön nyitva hagyott bejelentkezései megszűnnek); a megváltoztatott jelszavú mintafiókokat a rendszer már nem tiltja le. Az oktatók a kulcsátvételi PIN-kódjukat is maguk állíthatják be a **Profil → Kulcsátvételi PIN-kód** űrlapon; az adminisztrátor a felhasználólistában látja, kinek nincs még PIN-je.

### HTTPS éles üzemben

A konténerek magukban csak HTTP-t szolgálnak ki (a 3000-es porton). Éles üzemben a mellékelt
HTTPS-proxyt (`docker-compose.https.yml`, nginx 1.30) érdemes használni:

1. Tanúsítvány a `deploy/https/certs/` mappába `fullchain.pem` és `privkey.pem` néven (az intézménytől
   vagy pl. Let's Encryptből). Kipróbáláshoz önaláírt tanúsítvány:
   `./deploy/https/generate-self-signed-cert.sh kulcs.iskola.hu` (ekkor a `.env`-ben `HSTS_MAX_AGE=0`).
2. A `.env`-ben:
   ```
   PUBLIC_HOSTNAME=kulcs.iskola.hu
   ALLOWED_HOSTS=kulcs.iskola.hu;localhost;127.0.0.1;backend
   FRONTEND_BIND=127.0.0.1:3000
   COMPOSE_FILE=docker-compose.yml:docker-compose.https.yml
   ```
3. Indítás: `docker compose up -d --build`.

Mit állít be a proxy:
- a 80-as porton minden kérést `https://`-re irányít, a 443-on TLS 1.2/1.3-mal szolgál ki;
- **HSTS** (`Strict-Transport-Security: max-age=31536000`): a böngésző egy évig csak HTTPS-en keresi a
  címet. Csak érvényes tanúsítvánnyal kapcsold be véglegesen;
- a backendnek `Authentication__Cookie__SecureOnly=true` (a bejelentkezési süti csak HTTPS-en utazik) és
  `ForwardedHeaders__ForwardLimit=2`: a kliens és a backend között két megbízható proxy van (HTTPS-proxy,
  frontend nginx), így a backend a valódi kliens-IP-t látja. Enélkül a bejelentkezési kéréskorlát minden
  felhasználónál közös lenne (mindenki a proxy címéről jönne). A proxy a böngésző által küldött
  `X-Forwarded-For` fejlécet felülírja, így az nem hamisítható.

Ha az intézmény saját proxyja (IIS, Caddy, a gépen futó nginx) zárja le a HTTPS-t, az továbbítson a
`http://127.0.0.1:3000` címre, és állítson `X-Forwarded-Proto: https` és `X-Forwarded-For` fejlécet; a
`.env`-ben ekkor `AUTH_COOKIE_SECURE=true`, a backendnek pedig `ForwardedHeaders__ForwardLimit=2`, és ha a
proxy nem a Docker privát hálózatából érkezik, a címe a `ForwardedHeaders__KnownProxies` listába.

### Adatbázismentés és helyreállítás

A `db-backup` szolgáltatás induláskor, majd `BACKUP_INTERVAL_HOURS` óránként teljes, `CHECKSUM`-mal
ellenőrzött mentést készít (`BACKUP_DIR/KeyAndRoomDb_ÉÉÉÉHHNN_ÓÓPPMM.bak`), és törli a
`BACKUP_RETENTION_DAYS` napnál régebbieket. A mentés a gépen, az adatkötettől külön mappában van; innen
rendszeresen másold másik gépre vagy tárhelyre is. Linuxon a mappát az SQL Server felhasználójának
kell adni: `sudo chown 10001 ./backups`.

```bash
# Azonnali mentés
docker compose run --rm db-backup --once
# Helyreállítási próba (külön, ideiglenes adatbázisba; az éleshez nem nyúl)
docker compose run --rm db-backup restore-test
```

A helyreállítási próba visszaállítja a legutóbbi (vagy a megadott nevű) mentést a
`KeyAndRoomDb_RestoreTest` adatbázisba, lefuttatja a `DBCC CHECKDB`-t, összeveti a fő táblák sorszámát
az éles adatbázissal, majd törli az ideiglenes adatbázist. Havonta legalább egyszer futtasd.

Valódi visszaállítás (pl. adatvesztés után) – előtte állítsd le a backendet, hogy ne írjon közben:
```bash
docker compose stop backend db-backup
docker compose exec mssql bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "ALTER DATABASE [KeyAndRoomDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [KeyAndRoomDb] FROM DISK = N'"'"'/var/opt/mssql/backup/KeyAndRoomDb_20261125_102236.bak'"'"' WITH REPLACE; ALTER DATABASE [KeyAndRoomDb] SET MULTI_USER;"'
docker compose up -d
```

### Naplók és újraindulás

Minden hosszan futó szolgáltatás `restart: unless-stopped` beállítású (a gép vagy a Docker újraindulása
után magától elindul), a Docker-naplók szolgáltatásonként legfeljebb 5 × 10 MB-ot foglalnak
(`docker compose logs -f backend`).

### Elfelejtett jelszó és PIN

Önkiszolgáló jelszó-visszaállítás (e-mail) nincs. Aki elfelejtette a jelszavát, az adminisztrátortól kér
újat (a belépő oldal „Elfelejtetted a jelszavad?” gombja is ezt írja le): az admin a **Felhasználók →
Módosítás** alatt ideiglenes jelszót ad, amit a felhasználó az első belépés után a **Profil** oldalon
lecserél. A kulcsátvételi PIN az oktató titka: az admin nem állíthat be helyette PIN-t, csak **nullázhatja**
(pl. elfelejtett PIN esetén), utána az oktató a **Profil → Kulcsátvételi PIN-kód** űrlapon ad meg újat.

### Hosszú műszak és munkamenet

A bejelentkezés 8 óráig érvényes (`Jwt:ExpiresInHours`). Amíg a felhasználó aktív, a felület 5 percenként
megújítja (`POST /api/auth/refresh`), legfeljebb a bejelentkezéstől számított 14 óráig
(`Jwt:MaxSessionHours`); a lejárat előtt 10 perccel figyelmeztet.

### 2. Alkalmazás indítása konténerben
```bash
docker compose up --build -d
```

Az elindult szolgáltatások ellenőrzése:
```bash
docker compose ps
curl http://localhost:8000/api/health
```

### 3. Elérhetőségek és portok

| Szolgáltatás | Elérési cím | Leírás |
| :--- | :--- | :--- |
| **Frontend UI** | `http://localhost:3000` (HTTPS-proxyval: `https://<PUBLIC_HOSTNAME>`) | Webes felület (nginx szolgálja ki az éles buildet, az `/api` kéréseket továbbítja a backendre) |
| **Backend REST API** | `http://localhost:3000/api` | A frontend proxyján keresztül; közvetlenül (`http://localhost:8000`) csak a saját gépről érhető el |
| **Swagger UI** | `http://localhost:8000/swagger` | API dokumentáció (Development környezetben) |
| **MSSQL Adatbázis** | `localhost:1433` | SQL Server közvetlen elérés (DBeaver, Azure Data Studio stb.), csak a saját gépről |

Leállítás az adatok megőrzésével:
```bash
docker compose down
```

Az adatbázis az induláskor `APPLY_MIGRATIONS=true` beállítással futó backend
által alkalmazza a függő EF Core migrációkat. Meglévő helyi vagy megosztott
adatbázis frissítése előtt készíts és ellenőrizz mentést; a részletes, tizenkilenc
migrációt felsoroló leírást lásd a [database/README.md](./database/README.md)
fájlban. Éles vagy közös adatbázist ne frissíts automatikusan külön jóváhagyás
nélkül.

---

## Helyi fejlesztés és tesztelés

### Backend fejlesztés és tesztek (.NET 10)
A backend .NET 10 (LTS, támogatás 2028 novemberéig) alatt fut; helyi fejlesztéshez a .NET 10 SDK szükséges
(a `global.json` ezt rögzíti). Ha a gépen egy régebbi SDK is van (pl. `/usr/local/share/dotnet` alatt a 8-as),
a 10-es SDK mappáját tedd előre a `PATH`-ban, pl. a `~/.zshrc`-ben:
```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
```
```bash
# Fordítás
dotnet build ProjektLabor.sln

# Adatbázist nem igénylő tesztek
dotnet test ProjektLabor.sln --configuration Release \
  --filter "FullyQualifiedName!~SqlServerIntegrationTests"
```

Az SQL Server-integrációs tesztek kizárólag erre elkülönített teszt SQL
Server-példányon futtathatók. A teszt ideiglenes `ProjektLaborIntegration_*`
adatbázisokat hoz létre és töröl. Ha a `SQLSERVER_TEST_CONNECTION` környezeti
változó nincs beállítva, ezek a tesztek kihagyásra kerülnek (nem buknak el).

### Frontend fejlesztés (Node.js 22+)
```bash
cd frontend
npm ci
npm test
npm run lint
npm run build
```

Az oktató foglalásvége előtti emlékeztetőit a felület az adatbázisban tárolt
értesítésekként jeleníti meg. A napi kulcszárási háttérfolyamat a konfigurált
zárási határidőkor (alapértelmezetten 22:00, `Europe/Budapest`) a még kint lévő
kulcsokhoz az adatbázisban tárolt értesítést hoz létre minden `Portas`
felhasználónak. Az oktatók és portások a `/notifications` felületen olvashatják
értesítéseiket, és olvasottként jelölhetik őket. Az e-mail-, SMS- és push-küldés
nincs bekötve.

---

## Kapcsolódó dokumentációk

- [Projektterv és részletes megvalósítási leírás](./projektterv.md) – célok, üzleti szabályok, komponens-architektúra és roadmap.
- [REST API tervezési dokumentáció](./docs/API_DESIGN.md) – részletes végpont-leírások, kérés- és válaszformátumok (DTO-k).
- [Adatbázis és migrációs segédlet](./database/README.md) – EF Core migrációs parancsok és adatbázis megjegyzések.
