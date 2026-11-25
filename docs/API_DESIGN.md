# API_DESIGN.md – Egyetemi Kulcs- és Teremnyilvántartó Rendszer

> **Implementációs állapot (2026-11-19):** Ez a fejezet a backend jelenlegi működését írja le, és ez a mérvadó. A 2. fejezettől kezdődő részletes fejezetek a 2026-10-30-i tervből származnak; ahol eltérnek, az alábbi összefoglaló és a backend kódja az irányadó.

## Aktuális backend-összefoglaló

Az API ASP.NET Core 10 Minimal API, JWT-hitelesítéssel és szerepkör-alapú policykkel. Regisztrált végpontcsoportok: `/api/auth`, `/api/users`, `/api/rooms`, `/api/keys`, `/api/key-movements`, `/api/bookings`, `/api/tickets`, `/api/maintenance`, `/api/keys/{id}/master-access`, `/api/reliability`, `/api/notifications`, `/api/parameters`, `/api/audit`, `/api/reports` és `/api/health`.

### Hitelesítés, biztonság, hibakezelés

- **Bejelentkezés:** a `POST /api/auth/login` a JWT-t `projektlabor_auth` nevű **HttpOnly** sütiben állítja be (`SameSite=Strict`, `Path=/api`, HTTPS-en `Secure`, 8 órás lejárat, aktivitás közben `POST /api/auth/refresh`-sel megújítva, legfeljebb 14 óráig). A böngészős kliens a tokent nem tárolja; a munkamenetet a `GET /api/auth/me` állítja helyre, a `POST /api/auth/logout` törli a sütit. Az `Authorization: Bearer` fejléc továbbra is használható (Swagger, tesztek, más kliensek).
- **Token-ellenőrzés:** minden kérésnél az adatbázis alapján is ellenőrizzük, hogy a felhasználó létezik, aktív, és a szerepköre egyezik a tokenben lévővel – az inaktiválás és a szerepkörváltás azonnal hat. A jelszócsere (saját vagy adminisztrátori) a korábban kiadott tokeneket is azonnal érvényteleníti.
- **Kéréskorlát:** kliens-IP-nként percenként legfeljebb 10 bejelentkezési kísérlet (`429 Too Many Requests`). A frontend nginx proxyja mögött a valódi IP-t csak privát hálózatból érkező `X-Forwarded-For` alapján fogadjuk el.
- **CORS:** csak a `Cors:AllowedOrigins` (környezeti változóban `CORS_ALLOWED_ORIGINS`) listában szereplő originök, sütikkel együtt. A Docker Compose-os futtatásnál a frontend azonos originről, proxyn át éri el az API-t.
- **Bemenet-ellenőrzés:** minden végponton lefutnak a DTO-k ellenőrző attribútumai (`[Required]`, `[MaxLength]`, `[EmailAddress]`, `[Range]`, `[RegularExpression]`); hiba esetén `400` `{ message, errors }` válasz, az adatbázisig nem jut el a kérés.
- **Párhuzamos kérések:** holtpont vagy zár-időtúllépés esetén a tranzakciós műveletek legfeljebb háromszor automatikusan újrapróbálkoznak; ha így sem sikerül, `503` (`Retry-After: 2`) magyar üzenettel. Egyedi index ütközése (pl. két egyidejű kérés ugyanazzal a felhasználónévvel, kulcsszámmal vagy teremnévvel) `409 Conflict`.
- **Auditnapló művelettípusai:** egységesen nagybetűs angol kódok (pl. `KEY_ISSUE`, `ROLE_CHANGE`, `SYSTEM_PARAMETER_UPDATE`, `RELIABILITY_CORRECTION`, `AUTOMATIC_PENALTY`, `AUTOMATION_DATA_ISSUE`); a korábbi magyar szöveges bejegyzéseket a `HardenSchemaAndAuditNames` migráció átnevezi.
- **Váratlan hiba:** egységes `500` JSON válasz magyar `message` mezővel és `traceId`-val; a részletek csak a szervernaplóba kerülnek.
- **Audit:** minden módosító művelet, bejelentkezés, megtagadott hozzáférés (`ACCESS_UNAUTHENTICATED`, `ACCESS_FORBIDDEN`) és elutasított módosító kérés (`OPERATION_REJECTED`) naplózódik. A napló csak bővíthető. Anonim kérésekből IP-nként percenként legfeljebb 20 bejegyzés kerül be; a naplózás hibája nem rontja el a kliensnek adott választ.
- **Health-check:** a `GET /api/health` adatbázis-ellenőrzése legfeljebb 3 másodpercig tart (`HealthCheck:DatabaseTimeoutSeconds`), utána `503`.

### Időkezelés

- Az adatbázisban és az API-ban minden időpont **UTC**; a válaszok `Z` jelöléssel érkeznek. A megjelölés nélküli bemeneti időpontot az intézmény helyi idejének (`Automation:TimeZoneId`, alapértelmezés: `Europe/Budapest`) tekintjük.
- A nyitvatartási és idősáv-szabályokat (08:00–20:00, 45 perces rács, egy naptári nap) helyi idő szerint ellenőrizzük.

### Felhasználók

- `DELETE /api/users/{id}` **inaktivál** (nem töröl): az előzmények megmaradnak, az inaktív felhasználó nem tud belépni. `POST /api/users/{id}/reactivate` újraaktivál. Nem inaktiválható saját maga, az utolsó aktív adminisztrátor, aktív foglalással vagy kint lévő kulccsal rendelkező felhasználó.
- Szerepkörváltás (`PUT /api/users/{id}/role`): saját admin szerepkör és az utolsó aktív admin nem fokozható le. Oktatóról más szerepkörre csak akkor, ha nincs aktív jövőbeli foglalása és nincs nála kulcs; ilyenkor a mesterkulcs-jogosultságai megszűnnek.
- Jelszószabály (létrehozás és módosítás): legalább 12 karakter, legfeljebb 72 UTF-8 bájt. A felhasználónév és az e-mail-cím egyediségét a levágott (az e-mailnél kisbetűs) értéken ellenőrizzük.
- `DELETE /api/users/{id}/pin`: az átvevői PIN nullázása (adminisztrátor). Az admin nem állíthat be PIN-t; újat az oktató ad meg a `PUT /api/auth/pin` végponton. Nullázáskor a hibás próbálkozások száma és a PIN-zárolás is megszűnik. A PIN bcrypt hashként tárolódik, és nem jelenik meg válaszokban vagy a naplóban.
- A `UserDto` tartalmazza az `aktiv` mezőt; születési dátumot a rendszer nem tárol.

### Termek, karbantartás

- Terem deaktiválása (`DELETE /api/rooms/{id}` vagy `PUT` `aktivalva: false`): jövőbeli aktív foglalás mellett `409` `{ message, jovobeliFoglalasok }`. A `DELETE /api/rooms/{id}?foglalasokLemondasa=true` a foglalásokat lemondja (naplózva), majd deaktivál.
- Karbantartás deaktivált teremre nem rögzíthető, és ugyanarra a teremre egymást átfedő karbantartások sem. Foglalási ütközéskor `409` az ütköző foglalások listájával; az adminisztrátor lemondja vagy áthelyezi őket (`PUT /api/bookings/{id}`), majd újra rögzíti a karbantartást.

### Foglalások

- Létrehozás: múltbeli időpontra nem, hétvégére nem, legfeljebb 180 nappal előre, deaktivált teremre nem, és **csak oktató nevére** (adminként a `felhasznaloId`-val oktatót kell megadni). Egy oktatónak egyszerre legfeljebb 40 jövőbeli aktív foglalása lehet (`409`). Ütközés más aktív foglalással vagy karbantartással: `409` magyar üzenettel. Párhuzamos kérések közül teremszintű SQL Server-alkalmazászár és Serializable tranzakció mellett csak egy sikerül.
- Lemondás (`DELETE` vagy `PUT` `status: "Megszuntetve"`): csak aktív foglalás; megkezdett foglalást oktató nem mondhat le, adminisztrátor igen.
- Módosítás: megkezdett foglalás időpontja és terme nem módosítható; csak aktív foglalás helyezhető át.
- **Értesítés:** ha a foglalást nem az oktató maga, hanem az adminisztrátor mondja le vagy helyezi át, illetve karbantartás vagy a terem deaktiválása miatt szűnik meg, az oktató `BookingCancelled` / `BookingChanged` típusú értesítést kap, az `uzenet` mezőben az okkal.
- `GET /api/bookings/my`: a legújabbak elöl, kérésenként legfeljebb 500 foglalás (`page`, `pageSize`); az összes elem számát az `X-Total-Count` fejléc adja.
- Terem deaktiválása (`DELETE /api/rooms/{id}`) a foglalással közös teremzár alatt fut; amíg a terem kulcsa (vagy erre a teremre kiadott mesterkulcs) kint van, `409`.
- Státuszok: `Aktiv`, `Kesz` (lezajlott, kulcsfelvétellel – automatikus), `NoShow` (automatikus), `Megszuntetve`. A lemondott és a `NoShow` foglalás nem blokkol.

### Kulcsok és kulcsmozgások

- Kiadás (`POST /api/key-movements/issue`) és visszavétel (`POST /api/key-movements/return`): `Portas` jogosultság, átvevői PIN és teremállapot. Sérülés leírásakor automatikus `Sérülés` hibajegy készül – mesterkulcsnál arra a teremre, amelyre a kulcsot kiadták (`Kulcsmozgas.TeremId`).
- **PIN-zárolás:** 5 egymás utáni hibás PIN után a PIN 15 percre zárolódik (`429`). Hibás PIN-nél és hiányzó mesterkulcs-jogosultságnál `403` magyar `message` mezővel (pl. hány próbálkozás van még hátra).
- **Kényszerített visszavétel** (`POST /api/key-movements/force-return`, Portás vagy Adminisztrátor): ha a kulcs fizikailag visszakerült, de az átvevő nincs jelen, PIN nélkül, kötelező `indoklas` mezővel rögzíthető. A mozgás megjegyzése és az auditnapló (`KEY_FORCED_RETURN`) tartalmazza az indoklást.
- Kulcs csak aktív teremhez rögzíthető vagy rendelhető; a kulcs módosítása és deaktiválása a kiadással közös kulcszár alatt fut. A kulcslista kulcsonként csak a legutolsó mozgást kérdezi le.
- **Kiadás és foglalás:** foglaláshoz a kulcs legkorábban a kezdés előtt 2 órával, és legkésőbb a foglalás végéig adható ki. Ha a portás nem ad meg foglalást, a backend a kiadást az átvevő erre a teremre szóló, éppen esedékes aktív foglalásához köti; a válasz `foglalasId` mezője ezt jelzi.
- Inaktivált felhasználó nem vehet át kulcsot, és nem kaphat mesterkulcs-jogosultságot.
- Kint lévő kulcs nem deaktiválható, és típusa vagy terme nem módosítható (`409`); a kulcsszám ilyenkor is átírható. Kint lévő kulcs állapota mindig `Kiadva`.
- Mesterkulcs termei (`PUT /api/keys/{id}/rooms`): a teljes listát cseréli, de legalább egy terem kell. Régi, foglalás nélküli mesterkulcs-kiadásnál, ahol a kiadás terme nem ismert, visszavételkor a `teremId` megadása kötelező (a kulcslistában `kiadottTeremIsmeretlen: true`).
- **Határidő:** egy kulcsot a kiadás (helyi) napjának zárási időpontjáig (`napi_kulcsleadasi_zarasi_idopont`, alapból 22:00) kell visszahozni; zárás utáni kiadásnál a következő napéig. Ugyanezt a határidőt használja a lejárt kulcsok listája (`GET /api/key-movements/overdue`), az esti értesítés és a büntetőpont.

### Automatizmusok (percenként futó háttérfeldolgozók)

- **No-show:** ha a foglalás kezdete óta eltelt a türelmi idő (`no_show_idokorlat_perc`, 1–45 perc, alapból 15), és a foglaló nem vette fel a kulcsot, a foglalás `NoShow`. Kulcsfelvételnek számít a foglaláshoz kötött kiadás, ha a foglaló a terem kulcsát az idősávban (legfeljebb 2 órával korábban) felvette, illetve ha a kulcs a foglalás kezdetekor már nála volt (pl. egymás utáni foglalásoknál megtartotta). A feldolgozás Serializable tranzakcióban, zár alatt fut, így nem ütközik egy közben történő kulcskiadással. Amíg a terem kulcsa másnál van, a foglalás idején várunk; a foglalás vége után ilyenkor is `NoShow`.
- **Foglalás lezárása:** a véget ért aktív foglalás kulcsfelvétel esetén `Kesz` állapotot kap.
- **Napi kulcszárás:** a határidőig vissza nem hozott kulcsért `LeNemAdott` esemény (+3 büntetőpont) és minden aktív portásnak esti értesítés. Csak az utolsó 7 nap kiadásait és a még kint lévő kulcsokat dolgozza fel, kötegelt lekérdezésekkel.
- **Foglalásvégi értesítés:** a foglalás vége előtt 10 perccel értesítés az oktatónak.
- Minden automatikus művelet idempotens (`AutomationKey`), és naplózott.

### Megbízhatóság, értesítések, riportok, paraméterek

- Pontszám: a tanévi büntetőpontok időrendi összege 0 és 100 közé szorítva (a magasabb a rosszabb). `GET /api/reliability/users` – az összes aktív oktató pontszáma, a legrosszabb elöl (Adminisztrátor, Üzemeltetési Igazgató).
- `GET /api/notifications/my`: lapozható (`page`, `pageSize`, alapból 50, legfeljebb 100); a válasz lista, az összes elem számát az `X-Total-Count` fejléc adja. `csakOlvasatlan=true` mellett csak az olvasatlanok (a felület olvasatlan-jelvénye `pageSize=1` mellett az `X-Total-Count`-ot használja). A helyiség mesterkulcsnál a kiadás terme. `GET /api/notifications/pending`: az olvasatlan vagy küldés alatt álló értesítések, ugyanígy lapozva. Az értesítések `uzenet` mezője szöveges magyarázatot adhat (pl. a lemondás oka).
- Riportok: a napi/heti riport a `LeNemAdott` eseményeket késedelmes leadásként számolja; a termenkénti foglalásszámba a lemondott és a `NoShow` foglalás nem számít bele; mesterkulcsnál a kiadás terme számít.
- Terem-kihasználtság: a rendelkezésre álló idő a munkanapok nyitvatartása (08:00–20:00); a karbantartásból csak az erre eső rész vonódik le.
- Karbantartás: lezárult időszakra nem rögzíthető, legfeljebb 90 napos, a leírás legfeljebb 500 karakter; a már lezárult karbantartás nem módosítható és nem törölhető (`409`). Ütköző foglalásnál `409` a foglalások listájával; a kérés `lemondandoFoglalasIds` mezőjében megjelölt foglalásokat a backend a karbantartással **egy tranzakcióban** mondja le (értesítéssel), így részleges állapot nem maradhat.
- Rendszerparaméterek: a zárási időpont `HH:mm` formátumú, és nem lehet korábbi 20:00-nál; a no-show időkorlát 1–45 perc.
- Lapozás: `page=1` (legfeljebb 100 000, a túl nagy érték nem okoz hibát), `pageSize` legfeljebb 100; alapból 20 (foglalás, felhasználó, kulcsmozgás, hibajegy), illetve 50 (audit, saját értesítések). A válasz `totalCount` mezőt tartalmaz (az értesítéseknél az `X-Total-Count` fejlécet).
- Hibajegyek: kategória csak a rögzített listából (`Technikai hiba`, `Bútorzat / Felszerelés`, `Zár / Kulcs probléma`, `Takarítási igény`, `Egyéb észrevétel`, `Sérülés`); státuszváltás: `Nyitva` → `Folyamatban` / `Megoldva` / `Zarva`; `Folyamatban` → `Nyitva` / `Megoldva` / `Zarva`; `Megoldva` → `Nyitva` / `Zarva`; a `Zarva` végleges (`409`).

### Kezdő adatok

A migrációk minden adatbázisba felveszik szerepkörönként a közös kezdő fiókokat (`admin`, `oktato`, `portas`, `igazgato`), valamint egy mintaoktatót előzményekkel (kulcskiadások, foglalások, büntetőpontok). A korábbi `demo-*` fiókokat inaktiválják. Production környezetben a JWT issuer, audience és legalább 32 bájtos aláírókulcs konfigurálása kötelező; minta- és forráskódbeli kulcsokkal a backend nem indul el.

### Aktuális státuszkódok

Sikeres létrehozás `201 Created`, lekérdezés/módosítás `200 OK`, PIN-beállítás `204 No Content`, hiányzó rekord `404 Not Found`, hibás kérés `400 Bad Request`, jogosultsági hiba `401/403`, üzleti ütközés vagy duplikáció `409 Conflict`, túl sok kérés (bejelentkezés, PIN) `429 Too Many Requests`, adatbázis-elérhetetlenség a health-checknél `503`. A konkrét DTO-kat a `backend/DTOs` könyvtár és a Swagger séma tartalmazza.

> **Korábbi tervezeti részletek:** A következő fejezetek 2026-10-30-i tervből származnak. Az útvonal-, státusz- és jogosultsági táblázataik tervezeti/historikus adatok, nem feltétlenül egyeznek a mai implementációval; az aktuális viselkedést a fenti összefoglaló és a backend kódja határozza meg.
>
> **Verzió:** 1.0 – 3. bemutató előkészítő tervezet  
> **Dátum:** 2026-10-30  
> **Státusz:** Korábbi terv, nem aktuális API-szerződés

---

## Tartalomjegyzék

1. [Architekturális áttekintés](#1-architekturális-áttekintés)
2. [Teljes API-végpontlista](#2-teljes-api-végpontlista)
3. [API-k részletes specifikációja](#3-api-k-részletes-specifikációja)
4. [Szerepkör- és jogosultsági mátrix](#4-szerepkör--és-jogosultsági-mátrix)
5. [Request és Response DTO-k tervezete](#5-request-és-response-dto-k-tervezete)
6. [Adatbázis-kapcsolatok](#6-adatbázis-kapcsolatok)
7. [Validációs és hibakezelési szabályok](#7-validációs-és-hibakezelési-szabályok)
8. [Meglévő és hiányzó funkciók összesítése](#8-meglévő-és-hiányzó-funkciók-összesítése)
9. [Prioritási sorrend](#9-prioritási-sorrend)
10. [Ellentmondások a projektterv és a kód között](#10-ellentmondások-a-projektterv-és-a-kód-között)

---

## 1. Architekturális áttekintés

### 1.1. Aktuális implementáció

A backend endpoint-modulokra bontott **ASP.NET Core Minimal API**. A JWT Bearer-hitelesítés és a szerepkör-alapú policyk regisztrálva vannak; a védett útvonalak jogosultsága endpointonként van beállítva.

### 1.2. Tervezeti architektúra (korábbi terv)

```
React Frontend (port 3000)
        │
        │ HTTP/REST + JWT Bearer token
        ▼
ASP.NET Core Minimal API (port 8000)
        │
        ├── JWT Authentication Middleware
        ├── RBAC Authorization Middleware
        │
        ├── /api/auth/*          – Autentikáció
        ├── /api/users/*         – Felhasználókezelés
        ├── /api/rooms/*         – Termek (részben kész)
        ├── /api/keys/*          – Kulcsok
        ├── /api/key-movements/* – Kulcsmozgások (kiadás/visszavétel)
        ├── /api/bookings/*      – Foglalások
        ├── /api/tickets/*       – Hibajegyek
        ├── /api/maintenance/*   – Karbantartási idősávok
        ├── /api/reliability/*   – Megbízhatósági pontszám
        ├── /api/audit/*         – Audit log
        ├── /api/parameters/*    – Rendszerparaméterek
        ├── /api/reports/*       – Riportok
        ├── /api/notifications/* – Értesítések
        └── /api/health          – Egészségellenőrzés (kész)
                │
                ▼
        Entity Framework Core
                │
                ▼
        Microsoft SQL Server
```

### 1.3. Szerepkörök string értékei (kódban)

A `Felhasznalo.Role` mező a következő értékeket veheti fel:

| Szerepkör | Ajánlott string érték |
|---|---|
| Oktató / Igénylő | `"Oktato"` |
| Portás / Operátor | `"Portas"` |
| Adminisztrátor | `"Adminisztrator"` |
| Üzemeltetési Igazgató | `"UzemeltetesiIgazgato"` |

> A `Role` mező tárolási típusa `string`; az alkalmazás szerepkör-konstansokat és szerepkör-ellenőrzéseket használ.

---

## 2. API-végpontlista

A végpontlista a 2026-11-19-i állapotra frissítve; a részletes viselkedést a fenti aktuális összefoglaló írja le.

### Autentikáció

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| A1 | POST | `/api/auth/login` | Bejelentkezés, JWT token kiadás | ✅ Kész |
| A2 | POST | `/api/auth/logout` | Kijelentkezés | ✅ Kész |
| A3 | GET | `/api/auth/me` | Bejelentkezett felhasználó adatai | ✅ Kész |
| A4 | POST | `/api/auth/change-password` | Saját jelszó cseréje | ✅ Kész |
| A5 | PUT | `/api/auth/pin` | Saját kulcsátvételi PIN beállítása (oktató) | ✅ Kész |
| A6 | POST | `/api/auth/refresh` | Munkamenet megújítása (csúszó lejárat, legfeljebb `Jwt:MaxSessionHours`, alapból 14 óra a belépéstől); válasz: `expiresAt`, `sessionEndsAt` | ✅ Kész |

### Felhasználókezelés

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| U1 | GET | `/api/users` | Összes felhasználó listázása | ✅ Kész |
| U2 | GET | `/api/users/{id}` | Egy felhasználó adatainak lekérdezése | ✅ Kész |
| U3 | POST | `/api/users` | Új felhasználó létrehozása | ✅ Kész |
| U4 | PUT | `/api/users/{id}` | Felhasználó adatainak módosítása | ✅ Kész |
| U5 | DELETE | `/api/users/{id}` | Felhasználó inaktiválása (soft delete) | ✅ Kész |
| U6 | PUT | `/api/users/{id}/role` | Felhasználó szerepkörének módosítása | ✅ Kész |
| U7 | POST | `/api/users/{id}/reactivate` | Inaktivált felhasználó újraaktiválása | ✅ Kész |
| U8 | DELETE | `/api/users/{id}/pin` | Kulcsátvételi PIN nullázása (újat az oktató állít be) | ✅ Kész |

### Termek

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| R1 | GET | `/api/rooms` | Összes terem listázása | ✅ Kész |
| R2 | GET | `/api/rooms/{id}` | Egy terem adatainak lekérdezése | ✅ Kész |
| R3 | POST | `/api/rooms` | Új terem létrehozása | ✅ Kész |
| R4 | PUT | `/api/rooms/{id}` | Terem adatainak módosítása | ✅ Kész |
| R5 | DELETE | `/api/rooms/{id}` | Terem deaktiválása (`?foglalasokLemondasa=true`: jövőbeli foglalások lemondásával) | ✅ Kész |
| R6 | GET | `/api/rooms/{id}/availability` | Terem szabad időszakjainak lekérdezése | ✅ Kész |
| R7 | GET | `/api/rooms/{id}/keys` | Teremhez tartozó kulcsok listázása | ✅ Kész |

### Kulcsok

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| K1 | GET | `/api/keys` | Összes kulcs listázása | ✅ Kész |
| K2 | GET | `/api/keys/{id}` | Egy kulcs adatainak lekérdezése | ✅ Kész |
| K3 | POST | `/api/keys` | Új kulcs rögzítése | ✅ Kész |
| K4 | PUT | `/api/keys/{id}` | Kulcs adatainak módosítása | ✅ Kész |
| K5 | DELETE | `/api/keys/{id}` | Kulcs deaktiválása | ✅ Kész |
| K6 | GET | `/api/keys/{id}/status` | Kulcs aktuális állapota (kinél van, szabad-e) | ✅ Kész |
| K7 | GET | `/api/keys/{id}/movements` | Kulcs mozgástörténete | ✅ Kész |

### Mesterkulcs-teremhozzárendelések

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| KR1 | GET | `/api/keys/{id}/rooms` | Mesterkulcs termeinek listázása (Portás/Adminisztrátor/Üzemeltetési Igazgató) | ✅ Kész |
| KR2 | PUT | `/api/keys/{id}/rooms` | Mesterkulcs termeinek teljes cseréje (Adminisztrátor) | ✅ Kész |

### Mesterkulcs-jogosultságok

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| MK1 | GET | `/api/keys/{id}/master-access` | Kulcs mesterkulcs-jogosultsági listája | ✅ Kész |
| MK2 | POST | `/api/keys/{id}/master-access` | Mesterkulcs-jogosultság hozzáadása | ✅ Kész |
| MK3 | DELETE | `/api/keys/{id}/master-access/{userId}` | Mesterkulcs-jogosultság visszavonása | ✅ Kész |

### Kulcsmozgások (kiadás / visszavétel)

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| KM1 | POST | `/api/key-movements/issue` | Kulcs kiadásának rögzítése | ✅ Kész |
| KM2 | POST | `/api/key-movements/return` | Kulcs visszavételének rögzítése | ✅ Kész |
| KM2b | POST | `/api/key-movements/force-return` | Kényszerített visszavétel PIN nélkül, indoklással | ✅ Kész |
| KM3 | GET | `/api/key-movements` | Kulcsmozgások listázása (szűrőkkel) | ✅ Kész |
| KM4 | GET | `/api/key-movements/{id}` | Egy kulcsmozgás részletei | ✅ Kész |
| KM5 | GET | `/api/key-movements/overdue` | Lejárt / le nem adott kulcsok listája | ✅ Kész |

### Foglalások

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| B1 | GET | `/api/bookings` | Foglalások listázása (szűrőkkel) | ✅ Kész |
| B2 | GET | `/api/bookings/{id}` | Egy foglalás részletei | ✅ Kész |
| B3 | POST | `/api/bookings` | Új foglalás létrehozása | ✅ Kész |
| B4 | PUT | `/api/bookings/{id}` | Foglalás módosítása | ✅ Kész |
| B5 | DELETE | `/api/bookings/{id}` | Foglalás törlése / lemondása | ✅ Kész |
| B6 | GET | `/api/bookings/my` | Saját foglalásaim listája | ✅ Kész |

### Hibajegyek

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| T1 | GET | `/api/tickets` | Hibajegyek listázása (Oktató: csak saját); `nyitott=true` csak a Nyitva/Folyamatban állapotúak (`pageSize=1`-gyel darabszámhoz) | ✅ Kész |
| T2 | GET | `/api/tickets/{id}` | Egy hibajegy részletei (Oktató: csak saját) | ✅ Kész |
| T3 | POST | `/api/tickets` | Új hibajegy rögzítése (Oktató/Portás/Adminisztrátor) | ✅ Kész |
| T4 | PUT | `/api/tickets/{id}/status` | Hibajegy státuszának frissítése | ✅ Kész |

### Karbantartási idősávok

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| M1 | GET | `/api/maintenance` | Karbantartási idősávok listázása | ✅ Kész |
| M2 | GET | `/api/maintenance/{id}` | Egy idősáv részletei | ✅ Kész |
| M3 | POST | `/api/maintenance` | Új karbantartási idősáv rögzítése | ✅ Kész |
| M4 | PUT | `/api/maintenance/{id}` | Karbantartási idősáv módosítása | ✅ Kész |
| M5 | DELETE | `/api/maintenance/{id}` | Karbantartási idősáv törlése | ✅ Kész |

### Megbízhatósági pontszám

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| RE1 | GET | `/api/reliability/users/{userId}` | Oktató megbízhatósági összesítője | ✅ Kész |
| RE2 | GET | `/api/reliability/users/{userId}/events` | Oktató pontozási eseményeinek listája | ✅ Kész |
| RE3 | GET | `/api/reliability/my` | Saját megbízhatósági összesítő | ✅ Kész |
| RE4 | POST | `/api/reliability/correction` | Adminisztrátori manuális korrekció | ✅ Kész |
| RE5 | GET | `/api/reliability/users` | Az összes aktív oktató pontszáma, a legrosszabb elöl | ✅ Kész |

### Audit log

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| AL1 | GET | `/api/audit` | Audit log bejegyzések listázása (szűrőkkel) | ✅ Kész |
| AL2 | GET | `/api/audit/{id}` | Egy audit bejegyzés részletei | ✅ Kész |

### Rendszerparaméterek

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| P1 | GET | `/api/parameters` | Összes rendszerparaméter listázása | ✅ Kész |
| P2 | GET | `/api/parameters/{key}` | Egy rendszerparaméter lekérdezése | ✅ Kész |
| P3 | PUT | `/api/parameters/{key}` | Rendszerparaméter módosítása | ✅ Kész |

### Riportok

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| RP1 | GET | `/api/reports/daily` | Napi forgalmi kimutatás | ✅ Kész |
| RP2 | GET | `/api/reports/weekly` | Heti forgalmi kimutatás | ✅ Kész |
| RP3 | GET | `/api/reports/room-utilization` | Teremkihasználtsági statisztika | ✅ Kész |
| RP4 | GET | `/api/reports/key-traffic` | Kulcsforgalmi statisztika | ✅ Kész |
| RP5 | GET | `/api/reports/reliability` | Megbízhatósági statisztika (rendszerszintű) | ✅ Kész |

### Értesítések

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| N1 | GET | `/api/notifications/my` | Oktató és Portás saját értesítései (lapozható, `X-Total-Count`) | ✅ Kész |
| N3 | PATCH | `/api/notifications/{id}/read` | Saját értesítés olvasottként jelölése | ✅ Kész |
| N2 | GET | `/api/notifications/pending` | Olvasatlan vagy küldés alatt álló értesítések (Adminisztrátor / Portás) | ✅ Kész |

### Egészségellenőrzés

| # | Metódus | Endpoint | Funkció | Státusz |
|---|---|---|---|---|
| H1 | GET | `/api/health` | Alkalmazás és adatbázis állapota | ✅ Kész |


---

## 3. API-k részletes specifikációja

---

### A1 – POST `/api/auth/login`

**Funkció:** Felhasználói bejelentkezés. Sikeres azonosítás esetén JWT Bearer tokent ad vissza.

**Jogosultság:** Nyilvános (nem kell token)

**Request DTO (`LoginRequestDto`):**
```json
{
  "username": "string (kötelező, max 50 karakter)",
  "jelszo":   "string (kötelező, max 200 karakter; a jelszószabályt a beállításkor ellenőrizzük)"
}
```

**Response DTO (`LoginResponseDto`) – 200 OK:**
```json
{
  "expiresAt": "datetime (ISO 8601, UTC)",
  "felhasznalo": {
    "id":     "int",
    "nev":    "string",
    "email":  "string",
    "role":   "string",
    "hasPin": "bool"
  }
}
```
A JWT **csak** a `projektlabor_auth` HttpOnly sütiben érkezik, a JSON-válaszban nem (így JavaScriptből nem olvasható ki). Nem létező felhasználónévnél is lefut egy azonos költségű jelszó-ellenőrzés, hogy a válaszidő ne árulja el a felhasználónév létezését. A nyilvános mintajelszóval (lásd README) csak `SampleAccounts:Enabled=true` mellett lehet belépni.

**Kapcsolódó entitások:** `Felhasznalok`

**Validáció:**
- `username` és `jelszo` kötelező, nem üres
- Felhasználónév egyezés a `Felhasznalok` táblában
- BCrypt hash-ellenőrzés a `JelszoHash` mezővel szemben

**HTTP státuszkódok:**
- `200 OK` – sikeres bejelentkezés
- `400 Bad Request` – hiányzó / érvénytelen mezők
- `401 Unauthorized` – hibás felhasználónév vagy jelszó

**Szükséges tesztek:**
- Sikeres bejelentkezés érvényes adatokkal → 200 + token
- Hibás jelszó → 401
- Nem létező felhasználó → 401
- Hiányzó mezők → 400

---

### A2 – POST `/api/auth/logout`

**Funkció:** Kijelentkezés. JWT stateless természete miatt a kliens oldali token törlése az elsődleges mechanizmus; opcionálisan szerver oldali blacklist is megvalósítható.

**Jogosultság:** Autentikált felhasználó (bármely szerepkör)

**Request:** Üres body, Authorization header szükséges

**Response – 200 OK:**
```json
{ "message": "Sikeres kijelentkezés." }
```

**HTTP státuszkódok:**
- `200 OK` – sikeres kijelentkezés
- `401 Unauthorized` – érvénytelen token

---

### A3 – GET `/api/auth/me`

**Funkció:** A bejelentkezett felhasználó saját adatainak lekérdezése a JWT tokenből.

**Jogosultság:** Autentikált felhasználó (bármely szerepkör)

**Response DTO (`CurrentUserDto`) – 200 OK:**
```json
{
  "id":       "int",
  "nev":      "string",
  "email":    "string",
  "username": "string",
  "role":     "string"
}
```

**Kapcsolódó entitások:** `Felhasznalok`

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`

---

### A4 – POST `/api/auth/change-password`

**Funkció:** A bejelentkezett felhasználó saját jelszavának cseréje. A jelenlegi jelszót ellenőrzi; az új jelszóra ugyanaz a szabály vonatkozik, mint a felhasználó létrehozásakor (legalább 12 karakter, legfeljebb 72 UTF-8 bájt), és nem egyezhet a jelenlegivel. A bejelentkezéssel közös kéréskorlát (IP-nként percenként 10) védi. Auditnapló: `PASSWORD_CHANGE`, illetve hibás jelenlegi jelszónál `PASSWORD_CHANGE_FAILED`.

**Jogosultság:** Autentikált felhasználó (bármely szerepkör)

**Request DTO (`ChangePasswordRequestDto`):**
```json
{
  "jelenlegiJelszo": "string",
  "ujJelszo":        "string"
}
```

**HTTP státuszkódok:** `200 OK`, `400 Bad Request` (hibás jelenlegi jelszó vagy gyenge új jelszó), `401 Unauthorized`, `429 Too Many Requests`

Sikeres csere után a felhasználó **minden korábban kiadott tokenje érvénytelen** (a token `pwv` jelszóverzió-claimje már nem egyezik); az aktuális munkamenet a válaszban kapott új sütivel folytatódik. Ugyanez történik, ha az adminisztrátor állít be új jelszót (`PUT /api/users/{id}`).

---

### A5 – PUT `/api/auth/pin`

**Funkció:** Az oktató saját kulcsátvételi PIN-kódjának beállítása (a portán a kulcs átvételekor kell megadni). A nyitva hagyott munkamenet visszaélése ellen a jelenlegi jelszót is kéri. Új PIN-nél a hibás próbálkozások száma és a PIN-zárolás nullázódik. Kéréskorlát: mint a bejelentkezésnél. Auditnapló: `PIN_CHANGE`, hibás jelszónál `PIN_CHANGE_FAILED`.

**Jogosultság:** `Oktato`

**Request DTO (`SetOwnPinRequestDto`):**
```json
{
  "jelenlegiJelszo": "string",
  "pin":             "string (4–8 számjegy)"
}
```

**HTTP státuszkódok:** `200 OK`, `400 Bad Request` (hibás jelszó vagy PIN-formátum), `401 Unauthorized`, `403 Forbidden`, `429 Too Many Requests`

---

### U1 – GET `/api/users`

**Funkció:** Az összes felhasználó listázása.

**Jogosultság:** `Adminisztrator`

**Query paraméterek:**
- `role` (opcionális): szűrés szerepkör szerint
- `aktiv` (opcionális): `true` csak aktív, `false` csak inaktív felhasználók
- `kereses` (opcionális): szabad szöveges keresés névben, felhasználónévben és e-mail-címben (kis- és nagybetűtől függetlenül)
- A lista név szerint rendezett.
- `page` (opcionális, default: 1), `pageSize` (opcionális, default: 20)

**Response DTO (`UserListDto`) – 200 OK:**
```json
{
  "items": [
    { "id": "int", "nev": "string", "username": "string", "email": "string", "role": "string", "aktiv": "bool", "hasPin": "bool", "createdAt": "datetime" }
  ],
  "totalCount": "int", "page": "int", "pageSize": "int"
}
```

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Admin → 200; más szerepkör → 403; token nélkül → 401; szűrés szerepkör szerint.

---

### U3 – POST `/api/users`

**Funkció:** Új felhasználói fiók létrehozása.

**Jogosultság:** `Adminisztrator`

**Request DTO (`CreateUserRequestDto`):**
```json
{
  "username":       "string (kötelező, 3–50 karakter, csak A–Z, a–z, 0–9, pont, kötőjel, aláhúzás; egyedi)",
  "email":          "string (kötelező, max 100, egyedi, email formátum)",
  "jelszo":         "string (kötelező, min 8 karakter)",
  "nev":            "string (kötelező, max 100)",
  "role":           "string (kötelező: Oktato|Portas|Adminisztrator|UzemeltetesiIgazgato)"
}
```

**Response DTO (`UserDto`) – 201 Created:**
```json
{ "id": "int", "nev": "string", "username": "string", "email": "string", "role": "string" }
```

**Kapcsolódó entitások:** `Felhasznalok`

**Validáció:**
- `username` és `email` egyediség (unique index a DB-ben)
- `role` csak megengedett értéket vehet fel
- Jelszó BCrypt hash-elésre kerül
- Kötelező audit log bejegyzés: `CREATE`, `Felhasznalo`

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `409 Conflict`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Sikeres létrehozás → 201; duplikált username → 409; duplikált email → 409; érvénytelen role → 400; nem admin → 403.

---

### U4 – PUT `/api/users/{id}`

**Funkció:** Felhasználó adatainak módosítása (név, email, jelszó, születési dátum).

**Jogosultság:** `Adminisztrator`

**Request DTO (`UpdateUserRequestDto`):**
```json
{
  "email":          "string (opcionális, email formátum)",
  "nev":            "string (opcionális, max 100)",
  "ujJelszo":       "string (opcionális, min 8 karakter)"
}
```

**Validáció:** Email egyediség (ha módosul); audit log bejegyzés a módosított mezőkre.

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `409 Conflict`, `401 Unauthorized`, `403 Forbidden`

---

### U6 – PUT `/api/users/{id}/role`

**Funkció:** Felhasználó szerepkörének megváltoztatása.

**Jogosultság:** `Adminisztrator`

**Request DTO (`ChangeRoleRequestDto`):**
```json
{ "ujRole": "string (kötelező: Oktato|Portas|Adminisztrator|UzemeltetesiIgazgato)" }
```

**Validáció:** Érvényes szerepkör; kötelező audit log bejegyzés (ki, mikor, melyik user, régi és új role).

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### R1 – GET `/api/rooms` ✅ (KÉSZ – auth szükséges)

**Funkció:** Termek listázása.

**Jelenlegi állapot:** Implementált, de autentikáció és jogosultságellenőrzés nélkül.

**Szükséges módosítás:** JWT `.RequireAuthorization()` hozzáadása (bármely szerepkör).

**Query paraméterek (tervezett bővítés):** `aktivalva` (opcionális, bool)

**Jelenlegi Response DTO (`RoomDto`) – 200 OK:**
```json
[{ "id": "int", "nev": "string", "kapacitas": "int", "leiras": "string|null", "aktivalva": "bool" }]
```

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized` *(jelenleg nincs ellenőrizve)*, `500 Internal Server Error`

---

### R2 – GET `/api/rooms/{id}` ✅ (KÉSZ – auth szükséges)

**Funkció:** Egy terem részletes adatainak lekérdezése.

**Szükséges módosítás:** JWT `.RequireAuthorization()` hozzáadása.

**HTTP státuszkódok:** `200 OK`, `404 Not Found`, `401 Unauthorized` *(jelenleg nincs ellenőrizve)*, `500 Internal Server Error`

---

### R3 – POST `/api/rooms`

**Funkció:** Új terem létrehozása.

**Jogosultság:** `Adminisztrator`

**Request DTO (`CreateRoomRequestDto`):**
```json
{
  "nev":       "string (kötelező, max 100)",
  "kapacitas": "int (kötelező, min 1)",
  "leiras":    "string (opcionális, max 500)",
  "aktivalva": "bool (opcionális, default: true)"
}
```

**Response DTO:** `RoomDto` – 201 Created

**Kapcsolódó entitások:** `Termek`

**Validáció:** `nev` nem lehet üres; `kapacitas` >= 1

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Sikeres létrehozás → 201; hiányzó `nev` → 400; `kapacitas` < 1 → 400; nem admin → 403.

---

### R6 – GET `/api/rooms/{id}/availability`

**Funkció:** Egy terem szabad és foglalt időszakjainak lekérdezése adott dátumra.

**Jogosultság:** Autentikált felhasználó (bármely szerepkör)

**Query paraméterek:** `datum` (kötelező, `YYYY-MM-DD`)

**Response DTO (`RoomAvailabilityDto`) – 200 OK:**
```json
{
  "teremId": "int", "datum": "date",
  "szabad":       [{ "startTime": "datetime", "endTime": "datetime" }],
  "foglalt":      [{ "startTime": "datetime", "endTime": "datetime", "foglalasId": "int", "statusz": "string" }],
  "karbantartas": [{ "startTime": "datetime", "endTime": "datetime", "leiras": "string|null" }]
}
```

**Kapcsolódó entitások:** `Termek`, `Foglalasok`, `KarbantartasiIdosavok`

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`

---

### K1 – GET `/api/keys`

**Funkció:** Kulcsok listázása, opcionálisan szűrve.

**Jogosultság:** `Portas`, `Adminisztrator`, `UzemeltetesiIgazgato`

**Query paraméterek:** `teremId` (opcionális), `tipus` (opcionális), `aktivalva` (opcionális)

**Response DTO (`KeyDto`) – 200 OK:**
```json
[{
  "id": "int", "szam": "string", "tipus": "string", "aktivalva": "bool",
  "teremId": "int", "teremNev": "string",
  "jelenlegiAllapot": "string (Szabad|Kiadva|Deaktivalt)"
}]
```

**Kapcsolódó entitások:** `Kulcsok`, `Termek`, `Kulcsmozgasok`

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`

---

### K3 – POST `/api/keys`

**Funkció:** Új kulcs rögzítése.

**Jogosultság:** `Adminisztrator`

**Request DTO (`CreateKeyRequestDto`):**
```json
{
  "szam":      "string (kötelező, max 20, egyedi)",
  "tipus":     "string (kötelező: Mesterkulcs|Osztalykulcs)",
  "teremId":   "int (kötelező)",
  "aktivalva": "bool (opcionális, default: true)"
}
```

**Validáció:** `szam` egyediség; `teremId` létező teremre hivatkozik; `tipus` megengedett értékek.

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `404 Not Found`, `409 Conflict`, `401 Unauthorized`, `403 Forbidden`

---

### K6 – GET `/api/keys/{id}/status`

**Funkció:** Egy kulcs aktuális állapota (kinél van, mikor adták ki).

**Jogosultság:** `Portas`, `Adminisztrator`, `UzemeltetesiIgazgato`

**Response DTO (`KeyStatusDto`) – 200 OK:**
```json
{
  "kulcsId": "int", "szam": "string",
  "allapot": "string (Szabad|Kiadva|Deaktivalt)",
  "kiadasIdopontja": "datetime|null",
  "kiadtaOperatorNev": "string|null",
  "aktualisBirtokosNev": "string|null",
  "kapcsolodoFoglalasId": "int|null"
}
```

**Kapcsolódó entitások:** `Kulcsok`, `Kulcsmozgasok`

**HTTP státuszkódok:** `200 OK`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### KM1 – POST `/api/key-movements/issue`

**Funkció:** Kulcs kiadásának rögzítése (fizikai kulcsátadás dokumentálása).

**Jogosultság:** `Portas`

**Request DTO (`IssueKeyRequestDto`):**
```json
{
  "kulcsId":    "int (kötelező)",
  "foglalasId": "int (opcionális)",
  "atvevoFelhasznaloId": "int (foglalás nélkül kötelező, oktató azonosítója)",
  "megjegyzes": "string (opcionális, max 500)"
}
```

**Response DTO (`KeyMovementDto`) – 201 Created:**
```json
{
  "id": "int", "kulcsId": "int", "mozgasTipus": "Kiadás",
  "idopont": "datetime", "operatorId": "int", "operatorNev": "string",
  "foglalasId": "int|null", "atvevoFelhasznaloId": "int|null",
  "atvevoNev": "string|null", "megjegyzes": "string|null"
}
```

**Kapcsolódó entitások:** `Kulcsmozgasok`, `Kulcsok`, `Foglalasok`

**Validáció:**
- A kulcs jelenleg nem lehet „Kiadva" állapotban
- A kulcs aktív legyen
- Ha `foglalasId` megadott: a foglalás aktív legyen és a terem kulcsával egyezzen
- Foglalás esetén az átvevő a foglalás tulajdonosa; más átvevő nem adható meg
- Foglalás nélkül kötelező egy létező oktatót átvevőként rögzíteni
- `OperátorId` = a hitelesített token `sub` claimjéből vett portás ID-je
- `Idopont` = szerver által generált UTC időbélyeg
- Audit log bejegyzés

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Sikeres kiadás → 201; már kiadott kulcs → 400; nem létező kulcs → 404; nem portás → 403.

---

### KM2 – POST `/api/key-movements/return`

**Funkció:** Kulcs visszavételének rögzítése. A rendszer automatikusan meghatározza az esemény típusát és megbízhatósági pontot rögzít.

**Jogosultság:** `Portas`

**Request DTO (`ReturnKeyRequestDto`):**
```json
{ "kulcsId": "int (kötelező)", "megjegyzes": "string (opcionális, max 500)" }
```

**Response DTO (`KeyReturnResponseDto`) – 200 OK:**
```json
{
  "kulcsmozgasId": "int", "kulcsId": "int",
  "mozgasTipus": "Visszavétel", "idopont": "datetime",
  "megbizhatosagiEsemenyTipus": "string (IdobenLeadott|KesedelmesLeadott|LeNemAdott|Elhagyott)",
  "megbizhatosagiPont": "int (0, 1 vagy 3)"
}
```

**Kapcsolódó entitások:** `Kulcsmozgasok`, `Kulcsok`, `MegbizhatosagiEsemenyek`, `Rendszerparameterek`

**Üzleti logika:**
1. Megkeresi a legutóbbi „Kiadás" mozgást a kulcshoz
2. Lekéri a `napi_kulcsleadasi_zarasi_idopont` rendszerparamétert
3. Leadás <= foglalás vége → `IdobenLeadott` (0 pont)
4. Foglalás vége < leadás <= napi zárás → `KesedelmesLeadott` (+1 pont)
5. Napi zárás után → `LeNemAdott` (+3 pont)
6. Automatikusan `MegbizhatosagiEsemeny` rekord jön létre az oktatóhoz
7. A foglaláshoz kötött régi kulcsmozgások átvevője a foglalás tulajdonosából kerül helyreállításra
8. Foglalás és rögzített átvevő nélküli régi mozgásnál nincs automatikus pontlevonás; egyedi kulcsmozgás-azonosítóval auditjelzés készül

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Időben leadott → 0 pont; késedelmes → +1 pont; nem portás → 403; nem kiadott kulcs visszavétele → 400.

---

### KM5 – GET `/api/key-movements/overdue`

**Funkció:** Jelenleg kiadott, lejárt határidejű kulcsok listája.

**Jogosultság:** `Portas`, `Adminisztrator`, `UzemeltetesiIgazgato`

**Response DTO – 200 OK:**
```json
[{
  "kulcsId": "int", "szam": "string",
  "kiadasIdopontja": "datetime", "foglalasVege": "datetime|null",
  "elmaradtPercek": "int",
  "oktatoNev": "string", "oktatoId": "int",
  "statusz": "string (Kesedelmes|LeNemAdott)"
}]
```

**Kapcsolódó entitások:** `Kulcsok`, `Kulcsmozgasok`, `Foglalasok`, `Rendszerparameterek`

---

### B3 – POST `/api/bookings`

**Funkció:** Új teremfoglalás létrehozása. 45 perces időszeletekre épülő, ütközésvizsgálattal.

**Jogosultság:** `Oktato`

**Request DTO (`CreateBookingRequestDto`):**
```json
{
  "teremId":   "int (kötelező)",
  "startTime": "datetime (kötelező, ISO 8601 UTC)",
  "endTime":   "datetime (kötelező, ISO 8601 UTC)"
}
```

**Response DTO (`BookingDto`) – 201 Created:**
```json
{
  "id": "int", "teremId": "int", "teremNev": "string",
  "felhasznaloId": "int", "felhasznaloNev": "string",
  "startTime": "datetime", "endTime": "datetime",
  "statusz": "string", "createdAt": "datetime"
}
```

**Kapcsolódó entitások:** `Foglalasok`, `Termek`, `KarbantartasiIdosavok`

**Validáció:**
- `startTime` < `endTime`
- A tartam 45 perc többszöröse (45, 90, 135, ... perc)
- `startTime` nem lehet múltbeli
- A terem aktív legyen
- Nincs ütközés meglévő foglalásokkal (`AppDbContext.ValidateBookings()` alapján, de API szinten is ellenőrizendő)
- Nincs ütközés karbantartási idősávokkal
- `FelhasznaloId` = bejelentkezett felhasználó ID-je (token alapján)
- `Statusz` kezdetben: `"Aktiv"`

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `404 Not Found`, `409 Conflict`, `401 Unauthorized`, `403 Forbidden`

**Szükséges tesztek:** Sikeres foglalás → 201; ütköző foglalás → 409; nem 45 perc többszöröse → 400; múltbeli időpont → 400; karbantartással ütközik → 409; nem oktató → 403.

---

### B4 – PUT `/api/bookings/{id}`

**Funkció:** Foglalás időpontjának vagy termének módosítása.

**Jogosultság:** `Oktato` (csak saját), `Adminisztrator` (bármely)

**Request DTO (`UpdateBookingRequestDto`):**
```json
{ "teremId": "int (opcionális)", "startTime": "datetime (opcionális)", "endTime": "datetime (opcionális)" }
```

**Validáció:** Azonos ütközésvizsgálat mint B3-nál; oktató csak saját foglalását módosíthatja; csak `Aktiv` státuszú foglalás módosítható.

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `403 Forbidden`, `404 Not Found`, `409 Conflict`

---

### B5 – DELETE `/api/bookings/{id}`

**Funkció:** Foglalás lemondása / törlése.

**Jogosultság:** `Oktato` (csak saját), `Adminisztrator` (bármely)

**Üzleti logika:** Státusz `"Megszuntetve"`-re állítódik (soft delete); ha a foglaláshoz már kulcs volt kiadva, a törlés megtagadható vagy figyelmeztetéssel megy végbe.

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `403 Forbidden`, `404 Not Found`

---

### T3 – POST `/api/tickets`

**Funkció:** Hibajegy felvétele teremhez.

**Jogosultság:** `Oktato` (saját bejelentés), `Portas`, `Adminisztrator`

**Request DTO (`CreateTicketRequestDto`):**
```json
{
  "teremId":   "int (kötelező)",
  "kategoria": "string (kötelező, pl. Villamos|Vizvezeték|Butor|Egyeb)",
  "leiras":    "string (kötelező, min 10, max 1000 karakter)"
}
```

**Response DTO (`TicketDto`) – 201 Created:**
```json
{
  "id": "int", "teremId": "int", "teremNev": "string",
  "bejelentId": "int", "bejelentNev": "string",
  "kategoria": "string", "leiras": "string",
  "statusz": "string (Nyitva)", "createdAt": "datetime"
}
```

**Kapcsolódó entitások:** `Hibajegyek`, `Termek`, `Felhasznalok`

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### T4 – PUT `/api/tickets/{id}/status`

**Funkció:** Hibajegy státuszának frissítése.

**Jogosultság:** `Adminisztrator`

**Request DTO (`UpdateTicketStatusDto`):**
```json
{ "statusz": "string (kötelező: Nyitva|Folyamatban|Megoldva|Zarva)", "megjegyzes": "string (opcionális)" }
```

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### M3 – POST `/api/maintenance`

**Funkció:** Karbantartási idősáv rögzítése teremhez.

**Jogosultság:** `Adminisztrator`

**Request DTO (`CreateMaintenanceSlotRequestDto`):**
```json
{ "teremId": "int (kötelező)", "startTime": "datetime (kötelező)", "endTime": "datetime (kötelező)", "leiras": "string (opcionális, max 500)" }
```

**Response DTO (`MaintenanceSlotDto`) – 201 Created:**
```json
{
  "id": "int", "teremId": "int", "teremNev": "string",
  "startTime": "datetime", "endTime": "datetime", "leiras": "string|null",
  "utkozisFoglalasok": [{ "foglalasId": "int", "felhasznaloNev": "string", "startTime": "datetime", "endTime": "datetime" }]
}
```

**Kapcsolódó entitások:** `KarbantartasiIdosavok`, `Termek`, `Foglalasok`

**Üzleti logika:** Ha az idősáv ütközik meglévő foglalással, a rendszer **figyelmeztet** (nem tiltja), és visszaadja az érintett foglalásokat. Az adminisztrátornak döntenie kell az érintett foglalásokról.

**HTTP státuszkódok:** `201 Created` (+ `utkozisFoglalasok` ha van ütközés), `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`

---

### RE1 – GET `/api/reliability/users/{userId}`

**Funkció:** Egy oktató megbízhatósági összesítőjének lekérdezése.

**Jogosultság:** `Adminisztrator`, `UzemeltetesiIgazgato`

**Query paraméterek:** `tanev` (opcionális, default: aktuális tanév, pl. `"2026-2027"`)

**Response DTO (`ReliabilitySummaryDto`) – 200 OK:**
```json
{
  "felhasznaloId": "int", "felhasznaloNev": "string",
  "tanev": "string", "aktivPontszam": "int (0-100)",
  "kovetkezmeny": "string (Nincsen|Figyelmeztes|KotelezoElbeszelgetes|KotelezoTrening)",
  "esemenyek": [{
    "id": "int", "esemenyTipus": "string", "pont": "int",
    "esemenyIdopontja": "datetime",
    "kulcsId": "int|null", "foglalasId": "int|null", "indoklas": "string|null"
  }]
}
```

**Kapcsolódó entitások:** `MegbizhatosagiEsemenyek`, `Felhasznalok`

**Üzleti logika:**
- `aktivPontszam` = számított érték (nem tárolt): az aktuális tanév eseményei pontjainak összege, 0–100 közé szorítva
- `kovetkezmeny` küszöbök: 0–9 → Nincsen; 10–19 → Figyelmeztes; 20–29 → KotelezoElbeszelgetes; 30–100 → KotelezoTrening

**HTTP státuszkódok:** `200 OK`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### RE3 – GET `/api/reliability/my`

**Funkció:** A bejelentkezett oktató saját megbízhatósági összesítője.

**Jogosultság:** `Oktato`

**Response DTO:** Azonos `ReliabilitySummaryDto`-val

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`

---

### RE4 – POST `/api/reliability/correction`

**Funkció:** Adminisztrátori manuális korrekciós pontozási esemény rögzítése.

**Jogosultság:** `Adminisztrator`

**Request DTO (`ManualCorrectionRequestDto`):**
```json
{
  "felhasznaloId": "int (kötelező, érintett oktató)",
  "pont":          "int (kötelező, lehet negatív is)",
  "indoklas":      "string (kötelező, min 10 karakter)",
  "tanev":         "string (kötelező, pl. 2026-2027)"
}
```

**Response DTO – 201 Created:**
```json
{
  "id": "int", "esemenyTipus": "ManualisKorrekcio", "pont": "int",
  "indoklas": "string", "letrehozoNev": "string",
  "esemenyIdopontja": "datetime",
  "regiAktivPontszam": "int", "ujAktivPontszam": "int"
}
```

**Kapcsolódó entitások:** `MegbizhatosagiEsemenyek`, `AuditLogok`

**Validáció:**
- `felhasznaloId` `Oktato` szerepkörű felhasználó
- `indoklas` kötelező, nem üres
- Kötelező audit log bejegyzés: ki módosított, mikor, melyik oktatót, régi érték, új érték, indoklás

**HTTP státuszkódok:** `201 Created`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### AL1 – GET `/api/audit`

**Funkció:** Audit log bejegyzések listázása.

**Jogosultság:** `Adminisztrator`, `UzemeltetesiIgazgato`

**Query paraméterek:** `felhasznaloId`, `entitasTipus`, `muveletTipus`, `kezdet`, `veg`, `page`, `pageSize`

**Response DTO (`AuditLogDto`) – 200 OK:**
```json
{
  "items": [{
    "id": "int", "felhasznaloId": "int|null", "felhasznaloNev": "string|null",
    "muveletTipus": "string", "entitasTipus": "string",
    "entitasId": "string|null", "regiErtek": "string|null", "ujErtek": "string|null",
    "indoklas": "string|null", "letrehozasIdeje": "datetime"
  }],
  "totalCount": "int"
}
```

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`

**Szűrés és lapozás:** `felhasznaloId`, `entitasTipus`, `muveletTipus`, `kezdet`, `veg`, `page`, `pageSize`. A dátumhatárok inkluzívak, az alapértelmezett lapméret 50, a maximális 100.

> ⚠️ Az audit log bejegyzések **soha nem törölhetők és nem módosíthatók** – DELETE és PUT szándékosan nincs tervezve.

---

### AL2 – GET `/api/audit/{id}`

**Funkció:** Egy auditbejegyzés részleteinek lekérdezése.

**Jogosultság:** `Adminisztrator`, `UzemeltetesiIgazgato`

**Válasz:** Egy `AuditLogDto` bejegyzés.

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`

### P1 – GET `/api/parameters`

**Funkció:** Az összes rendszerparaméter listázása.

**Jogosultság:** `Adminisztrator`, `UzemeltetesiIgazgato`

**Válasz:** `RendszerparameterDto` objektumok listája.

### P2 – GET `/api/parameters/{key}`

**Funkció:** Egy rendszerparaméter lekérdezése.

**Jogosultság:** `Adminisztrator`, `UzemeltetesiIgazgato`

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`

### P3 – PUT `/api/parameters/{key}`

**Funkció:** Rendszerparaméter értékének módosítása.

**Jogosultság:** `Adminisztrator`

**URL paraméter:** `key` – pl. `napi_kulcsleadasi_zarasi_idopont`

**Request DTO (`UpdateParameterRequestDto`):**
```json
{ "ertek": "string (kötelező)", "indoklas": "string (opcionális)" }
```

**Response DTO (`RendszerparameterDto`) – 200 OK:**
```json
{ "kulcs": "string", "ertek": "string", "leiras": "string|null", "modositasIdeje": "datetime" }
```

**Validáció:**
- `napi_kulcsleadasi_zarasi_idopont` → `HH:mm` formátumú időpont (pl. `"22:00"`)
- `no_show_idokorlat_perc` → pozitív egész szám (percben)
- Kötelező audit log bejegyzés a régi és új értékkel
- Az auditbejegyzés és a paraméter módosítása egy adatbázis-mentésben történik

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `404 Not Found`, `401 Unauthorized`, `403 Forbidden`

---

### RP1 – GET `/api/reports/daily`

**Funkció:** Napi kulcs- és teremforgalmi kimutatás.

**Jogosultság:** `UzemeltetesiIgazgato`, `Adminisztrator`

**Query paraméterek:** `datum` (kötelező, `YYYY-MM-DD`)

**Response DTO (`DailyReportDto`) – 200 OK:**
```json
{
  "datum": "date",
  "osszesKulcskiadas": "int", "osszesKulcsvisszavetel": "int",
  "osszesKesedelmesFoglalas": "int", "osszesNoShow": "int",
  "termenkintiForgalom": [{ "teremId": "int", "teremNev": "string", "foglalasok": "int", "kulcskiadasok": "int" }]
}
```

**Kapcsolódó entitások:** `Kulcsmozgasok`, `Foglalasok`, `Termek`

**HTTP státuszkódok:** `200 OK`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`

---

### RP2 – GET `/api/reports/weekly`

**Funkció:** Heti kulcs- és teremforgalmi összesítő hétfőtől vasárnapig.

**Jogosultság:** `UzemeltetesiIgazgato`, `Adminisztrator`

**Query paraméter:** `datum` (opcionális, `YYYY-MM-DD`; a dátumot tartalmazó hétre készül a riport, alapértelmezés a jelenlegi hét).

**Válasz:** A hét kezdete/vége, kulcskiadások és -visszavételek, késedelmes leadások, no-show foglalások összesített száma, valamint termenként a foglalások és kulcskiadások száma.

### RP3 – GET `/api/reports/room-utilization`

**Funkció:** Aktív termek lefoglalt óráinak és kihasználtsági arányának lekérdezése.

**Jogosultság:** `UzemeltetesiIgazgato`, `Adminisztrator`

**Query paraméterek:** `kezdet` és `veg` (opcionálisan együtt, inkluzív `YYYY-MM-DD` dátumok; elhagyva az aktuális hónap kezdete és a mai nap). Legfeljebb 366 napos időszak kérhető.

**Számítás:** A lemondott és no-show foglalások nem számítanak bele. A rendelkezésre álló idő aktív termenként napi 24 óra.

### RP4 – GET `/api/reports/key-traffic`

**Funkció:** Kulcsonkénti kiadás-, visszavétel- és összes mozgásszám lekérdezése.

**Jogosultság:** `UzemeltetesiIgazgato`, `Adminisztrator`

**Query paraméterek:** `kezdet` és `veg` (opcionálisan együtt, inkluzív `YYYY-MM-DD` dátumok; elhagyva az aktuális hónap kezdete és a mai nap). Legfeljebb 366 napos időszak kérhető.

### RP5 – GET `/api/reports/reliability`

**Funkció:** Az aktuális tanév oktatói megbízhatósági eseményeinek és pontszámainak rendszerszintű összesítése.

**Jogosultság:** `UzemeltetesiIgazgato`, `Adminisztrator`

**Válasz:** Tanév, események száma, érintett oktatók száma, átlagos (0–100 közé korlátozott) összpontszám, valamint eseménytípusonkénti darabszám és pontösszeg.

**HTTP státuszkódok (RP2–RP4):** `200 OK`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`

**HTTP státuszkódok (RP5):** `200 OK`, `401 Unauthorized`, `403 Forbidden`

---

### N1 – GET `/api/notifications/my`

**Funkció:** A bejelentkezett oktató vagy portás címzett értesítéseinek listája.

**Jogosultság:** `Oktato`, `Portas`

**Response DTO – 200 OK:**
```json
[{
  "id": "int", "foglalasId": "int|null",
  "ertesitesTipus": "string (BeforeEnd|EveningKeyReturn)",
  "idoPont": "datetime",
  "allapot": "string (Kesz|KuldesAlatt|Hiba)",
  "createdAt": "datetime",
  "foglalasKezdete": "datetime|null", "foglalasVege": "datetime|null",
  "olvasva": "boolean", "kulcsSzam": "string|null",
  "teremNev": "string|null", "hatarido": "datetime|null"
}]
```

Az `EveningKeyReturn` típus a napi kulcszáráskor még kiadott kulcsot, a
helyiséget és a visszavételi határidőt jeleníti meg.

**Kapcsolódó entitások:** `Ertesitesek`, `Foglalasok`, `Felhasznalok`, `Kulcsmozgasok`

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`

---

### N2 – GET `/api/notifications/pending`

**Funkció:** A `KuldesAlatt` állapotú értesítések listája a belső kézbesítési folyamat számára.

**Jogosultság:** `Adminisztrator`, `Portas`.

**Válasz:** Ugyanaz az értesítés-DTO, mint az N1 végpontnál; tartalmazza a kapcsolódó foglalás azonosítóját, kezdetét és lejáratát is.

**HTTP státuszkódok:** `200 OK`, `401 Unauthorized`, `403 Forbidden`.

---

### N3 – PATCH `/api/notifications/{id}/read`

**Funkció:** A bejelentkezett felhasználó saját értesítését olvasottként jelöli.

**Jogosultság:** `Oktato`, `Portas`. Más felhasználó értesítése nem módosítható.

**Válasz:** `204 No Content`; a már olvasott értesítésre is idempotensen `204`.
Nem saját vagy nem létező értesítés esetén `404 Not Found`.

---

### H1 – GET `/api/health` ✅ (KÉSZ)

**Funkció:** Az alkalmazás és az adatbázis-kapcsolat állapotának ellenőrzése.

**Jogosultság:** Nyilvános

**Response – 200 OK:** `{ "status": "ok", "database": "connected" }`

**Response – 503 Service Unavailable:** `{ "status": "unhealthy", "database": "disconnected" }`


---

## 4. Szerepkör- és jogosultsági mátrix

| Endpoint | Oktató | Portás | Admin | Üzem.Ig. |
|---|:---:|:---:|:---:|:---:|
| **Auth** | | | | |
| POST /api/auth/login | ✅ | ✅ | ✅ | ✅ |
| POST /api/auth/logout | ✅ | ✅ | ✅ | ✅ |
| GET /api/auth/me | ✅ | ✅ | ✅ | ✅ |
| POST /api/auth/change-password | ✅ | ✅ | ✅ | ✅ |
| PUT /api/auth/pin | ✅ | ❌ | ❌ | ❌ |
| **Felhasználók** | | | | |
| GET /api/users | ❌ | ❌ | ✅ | ❌ |
| GET /api/users/{id} | ❌ | ❌ | ✅ | ❌ |
| POST /api/users | ❌ | ❌ | ✅ | ❌ |
| PUT /api/users/{id} | ❌ | ❌ | ✅ | ❌ |
| DELETE /api/users/{id} | ❌ | ❌ | ✅ | ❌ |
| PUT /api/users/{id}/role | ❌ | ❌ | ✅ | ❌ |
| **Termek** | | | | |
| GET /api/rooms | ✅ | ✅ | ✅ | ✅ |
| GET /api/rooms/{id} | ✅ | ✅ | ✅ | ✅ |
| POST /api/rooms | ❌ | ❌ | ✅ | ❌ |
| PUT /api/rooms/{id} | ❌ | ❌ | ✅ | ❌ |
| DELETE /api/rooms/{id} | ❌ | ❌ | ✅ | ❌ |
| GET /api/rooms/{id}/availability | ✅ | ✅ | ✅ | ✅ |
| GET /api/rooms/{id}/keys | ❌ | ✅ | ✅ | ✅ |
| **Kulcsok** | | | | |
| GET /api/keys | ❌ | ✅ | ✅ | ✅ |
| GET /api/keys/{id} | ❌ | ✅ | ✅ | ✅ |
| POST /api/keys | ❌ | ❌ | ✅ | ❌ |
| PUT /api/keys/{id} | ❌ | ❌ | ✅ | ❌ |
| DELETE /api/keys/{id} | ❌ | ❌ | ✅ | ❌ |
| GET /api/keys/{id}/status | ❌ | ✅ | ✅ | ✅ |
| GET /api/keys/{id}/movements | ❌ | ✅ | ✅ | ✅ |
| **Mesterkulcs-jogosultságok** | | | | |
| GET /api/keys/{id}/master-access | ❌ | ❌ | ✅ | ❌ |
| POST /api/keys/{id}/master-access | ❌ | ❌ | ✅ | ❌ |
| DELETE /api/keys/{id}/master-access/{uid} | ❌ | ❌ | ✅ | ❌ |
| **Kulcsmozgások** | | | | |
| POST /api/key-movements/issue | ❌ | ✅ | ❌ | ❌ |
| POST /api/key-movements/return | ❌ | ✅ | ❌ | ❌ |
| POST /api/key-movements/force-return | ❌ | ✅ | ✅ | ❌ |
| GET /api/key-movements | ❌ | ✅ | ✅ | ✅ |
| GET /api/key-movements/{id} | ❌ | ✅ | ✅ | ✅ |
| GET /api/key-movements/overdue | ❌ | ✅ | ✅ | ✅ |
| **Foglalások** | | | | |
| GET /api/bookings | ❌ | ✅ | ✅ | ✅ |
| GET /api/bookings/{id} | ✅* | ✅ | ✅ | ✅ |
| POST /api/bookings | ✅ | ❌ | ❌ | ❌ |
| PUT /api/bookings/{id} | ✅* | ❌ | ✅ | ❌ |
| DELETE /api/bookings/{id} | ✅* | ❌ | ✅ | ❌ |
| GET /api/bookings/my | ✅ | ❌ | ❌ | ❌ |
| **Hibajegyek** | | | | |
| GET /api/tickets | ❌ | ✅ | ✅ | ✅ |
| GET /api/tickets/{id} | ❌ | ✅ | ✅ | ✅ |
| POST /api/tickets | ❌ | ✅ | ✅ | ❌ |
| PUT /api/tickets/{id}/status | ❌ | ❌ | ✅ | ❌ |
| **Karbantartás** | | | | |
| GET /api/maintenance | ✅ | ✅ | ✅ | ✅ |
| GET /api/maintenance/{id} | ✅ | ✅ | ✅ | ✅ |
| POST /api/maintenance | ❌ | ❌ | ✅ | ❌ |
| PUT /api/maintenance/{id} | ❌ | ❌ | ✅ | ❌ |
| DELETE /api/maintenance/{id} | ❌ | ❌ | ✅ | ❌ |
| **Megbízhatóság** | | | | |
| GET /api/reliability/my | ✅ | ❌ | ❌ | ❌ |
| GET /api/reliability/users/{id} | ❌ | ❌ | ✅ | ✅ |
| GET /api/reliability/users/{id}/events | ❌ | ❌ | ✅ | ✅ |
| POST /api/reliability/correction | ❌ | ❌ | ✅ | ❌ |
| **Audit log** | | | | |
| GET /api/audit | ❌ | ❌ | ✅ | ✅ |
| GET /api/audit/{id} | ❌ | ❌ | ✅ | ✅ |
| **Rendszerparaméterek** | | | | |
| GET /api/parameters | ❌ | ❌ | ✅ | ✅ |
| GET /api/parameters/{key} | ❌ | ❌ | ✅ | ✅ |
| PUT /api/parameters/{key} | ❌ | ❌ | ✅ | ❌ |
| **Riportok** | | | | |
| GET /api/reports/daily | ❌ | ❌ | ✅ | ✅ |
| GET /api/reports/weekly | ❌ | ❌ | ✅ | ✅ |
| GET /api/reports/room-utilization | ❌ | ❌ | ✅ | ✅ |
| GET /api/reports/key-traffic | ❌ | ❌ | ✅ | ✅ |
| GET /api/reports/reliability | ❌ | ❌ | ✅ | ✅ |
| **Értesítések** | | | | |
| GET /api/notifications/my | ✅ | ✅ | ❌ | ❌ |
| PATCH /api/notifications/{id}/read | ✅ | ✅ | ❌ | ❌ |
| GET /api/notifications/pending | ❌ | ✅ | ✅ | ❌ |
| **Health** | | | | |
| GET /api/health | ✅ | ✅ | ✅ | ✅ |

> *`✅*` = csak saját erőforrásra vonatkozik*

---

## 5. Request és Response DTO-k tervezete

### Névtér: `ProjektLabor.DTOs`

Az alábbi DTO-k új fájlokba kerülnek a `backend/DTOs/` könyvtárban. A meglévő `RoomDto.cs` megtartandó.

### Auth DTO-k

```csharp
// DTOs/Auth/LoginRequestDto.cs
public class LoginRequestDto
{
    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    [Required, MinLength(6)]
    public string Jelszo { get; set; } = string.Empty;
}

// DTOs/Auth/LoginResponseDto.cs
public class LoginResponseDto
{
    [JsonIgnore] // csak a HttpOnly sütiben utazik
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public CurrentUserDto Felhasznalo { get; set; } = null!;
}

// DTOs/Auth/CurrentUserDto.cs
public class CurrentUserDto
{
    public int Id { get; set; }
    public string Nev { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
```

### Felhasználó DTO-k

```csharp
// DTOs/Users/CreateUserRequestDto.cs
public class CreateUserRequestDto
{
    [Required, MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    [Required, MaxLength(100), EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required, MinLength(8)]
    public string Jelszo { get; set; } = string.Empty;
    [Required, MaxLength(100)]
    public string Nev { get; set; } = string.Empty;
    [Required]
    public string Role { get; set; } = string.Empty;
}

// DTOs/Users/UserDto.cs
public class UserDto
{
    public int Id { get; set; }
    public string Nev { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

### Terem DTO-k (bővítés)

```csharp
// DTOs/Rooms/CreateRoomRequestDto.cs
public class CreateRoomRequestDto
{
    [Required, MaxLength(100)]
    public string Nev { get; set; } = string.Empty;
    [Required, Range(1, int.MaxValue)]
    public int Kapacitas { get; set; }
    [MaxLength(500)]
    public string? Leiras { get; set; }
    public bool Aktivalva { get; set; } = true;
}
// Megjegyzés: RoomDto.cs már létezik – megtartandó
```

### Kulcsmozgás DTO-k

```csharp
// DTOs/KeyMovements/IssueKeyRequestDto.cs
public class IssueKeyRequestDto
{
    [Required]
    public int KulcsId { get; set; }
    public int? FoglalasId { get; set; }
    public int? AtvevoFelhasznaloId { get; set; }
    [MaxLength(500)]
    public string? Megjegyzes { get; set; }
}

// DTOs/KeyMovements/ReturnKeyRequestDto.cs
public class ReturnKeyRequestDto
{
    [Required]
    public int KulcsId { get; set; }
    [MaxLength(500)]
    public string? Megjegyzes { get; set; }
}

// DTOs/KeyMovements/KeyMovementDto.cs
public class KeyMovementDto
{
    public int Id { get; set; }
    public int KulcsId { get; set; }
    public string MozgasTipus { get; set; } = string.Empty;
    public DateTime Idopont { get; set; }
    public int OperatorId { get; set; }
    public string OperatorNev { get; set; } = string.Empty;
    public int? FoglalasId { get; set; }
    public int? AtvevoFelhasznaloId { get; set; }
    public string? AtvevoNev { get; set; }
    public string? Megjegyzes { get; set; }
}
```

### Foglalás DTO-k

```csharp
// DTOs/Bookings/CreateBookingRequestDto.cs
public class CreateBookingRequestDto
{
    [Required]
    public int TeremId { get; set; }
    [Required]
    public DateTime StartTime { get; set; }
    [Required]
    public DateTime EndTime { get; set; }
}

// DTOs/Bookings/BookingDto.cs
public class BookingDto
{
    public int Id { get; set; }
    public int TeremId { get; set; }
    public string TeremNev { get; set; } = string.Empty;
    public int FelhasznaloId { get; set; }
    public string FelhasznaloNev { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Statusz { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

### Megbízhatóság DTO-k

```csharp
// DTOs/Reliability/ReliabilitySummaryDto.cs
public class ReliabilitySummaryDto
{
    public int FelhasznaloId { get; set; }
    public string FelhasznaloNev { get; set; } = string.Empty;
    public string Tanev { get; set; } = string.Empty;
    public int AktivPontszam { get; set; }
    public string Kovetkezmeny { get; set; } = string.Empty;
    public List<MegbizhatosagiEsemenyDto> Esemenyek { get; set; } = new();
}

// DTOs/Reliability/ManualCorrectionRequestDto.cs
public class ManualCorrectionRequestDto
{
    [Required]
    public int FelhasznaloId { get; set; }
    [Required]
    public int Pont { get; set; }
    [Required, MinLength(10), MaxLength(500)]
    public string Indoklas { get; set; } = string.Empty;
    [Required]
    public string Tanev { get; set; } = string.Empty;
}
```

---

## 6. Adatbázis-kapcsolatok

| Endpoint-csoport | Elsődleges entitás | Kapcsolódó entitások |
|---|---|---|
| `/api/auth/*` | `Felhasznalok` | – |
| `/api/users/*` | `Felhasznalok` | `AuditLogok` |
| `/api/rooms/*` | `Termek` | `Kulcsok`, `Foglalasok`, `KarbantartasiIdosavok`, `KulcsTerem` |
| `/api/keys/*` | `Kulcsok` | `Termek`, `KulcsTerem`, `MesterkulcsJogosultsagok`, `Kulcsmozgasok` |
| `/api/key-movements/*` | `Kulcsmozgasok` | `Kulcsok`, `Foglalasok`, `Felhasznalok`, `MegbizhatosagiEsemenyek`, `Rendszerparameterek` |
| `/api/bookings/*` | `Foglalasok` | `Termek`, `Felhasznalok`, `KarbantartasiIdosavok`, `Kulcsmozgasok`, `Ertesitesek` |
| `/api/tickets/*` | `Hibajegyek` | `Termek`, `Felhasznalok` |
| `/api/maintenance/*` | `KarbantartasiIdosavok` | `Termek`, `Felhasznalok`, `Foglalasok` |
| `/api/reliability/*` | `MegbizhatosagiEsemenyek` | `Felhasznalok`, `Kulcsok`, `Foglalasok`, `Kulcsmozgasok`, `AuditLogok` |
| `/api/audit/*` | `AuditLogok` | `Felhasznalok` |
| `/api/parameters/*` | `Rendszerparameterek` | `AuditLogok` |
| `/api/reports/*` | `Kulcsmozgasok`, `Foglalasok` | `Termek`, `Felhasznalok`, `MegbizhatosagiEsemenyek` |
| `/api/notifications/*` | `Ertesitesek` | `Foglalasok`, `Felhasznalok`, `Kulcsmozgasok`, `Kulcsok`, `Termek` |

### Megjegyzések az adatmodellhez

- Az `AppDbContext.ValidateBookings()` metódus már tartalmaz ütközésvizsgálatot, de **csak SaveChanges szinten** – az API rétegnek is célszerű előre ellenőrizni és értelmező hibaüzenetet visszaadni (`409 Conflict`).
- A `Kulcsmozgas.OperátorId` mező neve ékezetes karaktert tartalmaz. Ez a C# névkonvencióktól eltér – célszerű migrációval `OperatorId`-ra átnevezni.
- A `Foglalas.Status` mező szabad szöveges, enum-megkötés nincs. Megengedett értékek: `Aktiv`, `NoShow`, `Megszuntetve`, `Kesz`.
- A `MegbizhatosagiEsemeny.EsemenyTipus` megengedett értékei: `IdobenLeadott`, `KesedelmesLeadott`, `LeNemAdott`, `Elhagyott`, `ManualisKorrekcio`.

---

## 7. Validációs és hibakezelési szabályok

### 7.1. Globális hibakezelés

Minden végpontnál egységes hibakezelés szükséges:

```
500 Internal Server Error → ProblemDetails (title, detail)
400 Bad Request           → ValidationProblemDetails (errors mezőnként)
401 Unauthorized          → { "message": "Hitelesítés szükséges." }
403 Forbidden             → { "message": "Hozzáférés megtagadva." }
404 Not Found             → { "message": "<Entitás> nem található." }
409 Conflict              → { "message": "<Ütközés leírása>" }
```

### 7.2. JWT middleware ellenőrzési sorrend

1. Authorization header megléte és `Bearer <token>` formátum
2. Token érvényességi idő ellenőrzése
3. Signature ellenőrzés a szerver secret key-jével
4. `role` claim kiolvasása és RBAC ellenőrzés

### 7.3. Domain-specifikus validációk

| Szabály | Érintett végpont | Kezelés |
|---|---|---|
| Foglalás időtartama 45 perc többszöröse | POST /api/bookings | 400 Bad Request |
| Foglalás nem múltbeli időpontú | POST /api/bookings | 400 Bad Request |
| Foglalások nem ütköznek | POST, PUT /api/bookings | 409 Conflict |
| Kulcs nem adható ki, ha már kiadva | POST /api/key-movements/issue | 400 Bad Request |
| Kulcs visszavétele csak ha ki van adva | POST /api/key-movements/return | 400 Bad Request |
| Megbízhatósági pontszám max 100 (aktív érték) | POST /api/key-movements/return, POST /api/reliability/correction | Számítás korlátoz, esemény tényleges értékkel rögzül |
| Karbantartás és foglalás ütközése figyelmezteti az admint | POST /api/maintenance | 201 + `utkozisFoglalasok` mező |
| Manuális korrekciónál indoklás kötelező | POST /api/reliability/correction | 400 Bad Request |
| Audit log nem törölhető/módosítható | /api/audit/* | DELETE/PUT nem elérhető |
| `Role` mező csak megengedett értéket vehet fel | POST /api/users, PUT /api/users/{id}/role | 400 Bad Request |
| `napi_kulcsleadasi_zarasi_idopont` HH:mm formátumú | PUT /api/parameters/{key} | 400 Bad Request |
| `no_show_idokorlat_perc` pozitív egész | PUT /api/parameters/{key} | 400 Bad Request |

### 7.4. Saját erőforrás vs. más erőforrása

- **Oktató GET `/api/bookings/{id}`**: csak saját foglalása olvasható → más ID esetén `403 Forbidden`
- **Oktató PUT/DELETE `/api/bookings/{id}`**: csak saját foglalása módosítható/törölhető
- **Oktató GET `/api/reliability/my`**: csak saját adatai

---

## 8. Meglévő és hiányzó funkciók összesítése

**Állapot (2026-11-19):** a 2. fejezet végpontlistájának minden végpontja megvalósult és tesztelt (backend: 169 teszt, ebből 4 valódi SQL Serveren futó integrációs teszt; frontend: 28 teszt; mind a GitHub Actions CI-ben fut).

| Terület | Állapot |
|---|---|
| Hitelesítés (JWT HttpOnly sütiben, BCrypt jelszó- és PIN-hash, kéréskorlát) | ✅ Kész |
| RBAC (4 szerepkör, policyk, token-ellenőrzés az adatbázis alapján) | ✅ Kész |
| Foglalás, karbantartás, ütközésvizsgálat, speciális igények | ✅ Kész |
| Kulcskiadás/-visszavétel PIN-nel, mesterkulcsok, sérülésjegy | ✅ Kész |
| Automatizmusok (no-show, foglalás lezárása, napi kulcszárás, foglalásvégi értesítés) | ✅ Kész |
| Megbízhatósági pontrendszer, oktatónkénti lista, korrekció | ✅ Kész |
| Auditnapló (részletek, szűrés), riportok, rendszerparaméterek | ✅ Kész |
| Bemenet-ellenőrzés, egységes hibaválasz, health-check időkorláttal | ✅ Kész |
| Éles Docker-image-ek (ASP.NET runtime, nginx frontend) | ✅ Kész |

**Nem megvalósított, opcionális mérföldkövek:** külső értesítési csatorna (e-mail/SMS, M6), kereskedelmi SSL-tanúsítvány és külső naplógyűjtő (M7).

---

## 9. Prioritási sorrend

Az alábbi prioritási lista a korábbi bemutatóterv része, nem aktuális fejlesztési státusz.

### 🔴 P0 – 3. bemutató kötelező minimuma

| # | Végpont / Elem | Indoklás |
|---|---|---|
| 1 | **JWT middleware + RBAC** (infrastruktúra) | A többi védett végpont alap |
| 2 | **POST `/api/auth/login`** | Bejelentkezés nélkül semmi más nem működik |
| 3 | **GET `/api/auth/me`** | Bejelentkezett felhasználó azonosítása |
| 4 | **GET `/api/rooms`** (+ auth) | Már kész, csak auth kell hozzá |
| 5 | **GET `/api/rooms/{id}`** (+ auth) | Már kész, csak auth kell hozzá |
| 6 | **POST `/api/rooms`** | Törzsdatatok feltöltése |
| 7 | **GET `/api/keys`** | Kulcsnyilvántartás alapja |
| 8 | **POST `/api/keys`** | Kulcs rögzítése |
| 9 | **POST `/api/bookings`** | Foglalás létrehozása (ütközésvizsgálattal) |
| 10 | **GET `/api/bookings/my`** | Oktató saját foglalásai |
| 11 | **POST `/api/key-movements/issue`** | Kulcskiadás rögzítése |
| 12 | **POST `/api/key-movements/return`** | Kulcsvisszavétel + automatikus pontozás |
| 13 | **POST `/api/users`** | Felhasználók létrehozása (seed / admin UI) |

### 🟡 P1 – 3. bemutatóra erősen ajánlott

| # | Végpont | Indoklás |
|---|---|---|
| 14 | **GET `/api/bookings`** | Portás látja a foglalásokat kulcskiadáshoz |
| 15 | **GET `/api/bookings/{id}`** | Foglalás részletei |
| 16 | **DELETE `/api/bookings/{id}`** | Foglalás lemondása |
| 17 | **GET `/api/keys/{id}/status`** | Kulcs aktuális állapota |
| 18 | **GET `/api/users`** | Admin felhasználókezelés |
| 19 | **PUT `/api/users/{id}/role`** | Szerepkör-kezelés |
| 20 | **POST `/api/tickets`** | Hibajegy rögzítése |
| 21 | **GET `/api/reliability/my`** | Oktató saját pontszáma |
| 22 | **GET `/api/reliability/users/{id}`** | Admin/Igazgató látja az oktatók pontszámát |
| 23 | **GET `/api/key-movements/overdue`** | Késedelmes kulcsok jelzése |
| 24 | **GET `/api/rooms/{id}/availability`** | Terem szabad sávjai foglaláshoz |
| 25 | **GET `/api/parameters`** | Rendszerparaméterek megtekintése |
| 26 | **PUT `/api/parameters/{key}`** | Rendszerparaméter módosítása |

### 🟢 P2 – Később megvalósítható

| # | Végpont | Indoklás |
|---|---|---|
| 27 | PUT `/api/bookings/{id}` | Foglalás módosítása |
| 28 | POST `/api/reliability/correction` | Manuális admin korrekció |
| 29 | GET `/api/audit` | Audit log megtekintése |
| 30 | POST `/api/maintenance` | Karbantartási idősáv |
| 31 | GET `/api/reports/daily` | Napi riport |
| 32 | GET `/api/reports/weekly` | Heti riport |
| 33 | GET `/api/reports/room-utilization` | Kihasználtsági statisztika |
| 34 | GET `/api/reports/reliability` | Megbízhatósági statisztika |
| 35 | GET `/api/notifications/my` | Értesítések lekérdezése |
| 36 | POST `/api/auth/logout` | Token invalidálás |
| 37 | MK1–MK3 (mesterkulcs-jogosultságok) | Speciális kulcsjogosultságok |
| 38 | No-show automatizmus (`BackgroundService`) | ✅ Kész |
| 39 | Foglalás lejárta előtti értesítés | ✅ Kész |
| 40 | Napi zárási +3 büntetőpont | ✅ Kész |

---

## 10. Ellentmondások a projektterv és a kód között

Az alábbi megjegyzések a 2026-10-30-i terv és kód összevetését őrzik; az azóta megoldott ellentmondások nem jelentenek fennálló hibát.

### 10.1. ⚠️ Ékezetes mező neve a `Kulcsmozgas` modellben

**Projektterv:** Az entitás megfelel a tervnek.  
**Kód:** A `Kulcsmozgas.cs`-ben az `OperátorId` és `Operátor` navigációs property **ékezetes karaktert** tartalmaz (`á`). Ez a C# ajánlott névkonvencióktól eltér, és egyes eszközökkel (reflection, serializer) problémát okozhat.  
**Ajánlás:** Migrációval átnevezni `OperatorId`-ra (ékezet nélkül).

### 10.2. ✅ `Terem.Leiras` nullable eltérés – megoldva

**Állapot (2026-11-19):** a jelenlegi modell és adatbázis-séma szerint a `Leiras` oszlop nullable (`nvarchar(500)`), így az eltérés már nem áll fenn. Az alábbi leírás a korábbi állapotot őrzi.


**Modell (`Terem.cs`):** `[MaxLength(500)]` – nullable (nincs `[Required]`)  
**Migráció (`InitialCreate.cs`):** `nullable: false` – tehát `NOT NULL` a DB-ben  
**`RoomDto.cs`:** `string? Leiras` – nullable  
**Ütközés:** A migrált adatbázis `Leiras` oszlopa `NOT NULL`, de a modell és a DTO nullable-ként kezeli. Adatbevitelnél ez hibás működéshez vezethet.  
**Ajánlás:** Vagy a modellben kell `[Required]`-ot hozzáadni, vagy a migrációban `nullable: true`-ra javítani.

### 10.3. ✅ JWT-hitelesítés – a korábbi eltérés megoldva

**Jelenlegi kód:** A `Program.cs` regisztrálja a JWT Bearer-hitelesítést és az RBAC-policyket, a middleware pedig a védett végpontok előtt ellenőrzi a tokent. A `JwtSettings` a szükséges issuer-, audience- és aláírókulcs-konfigurációt is validálja.
**Állapot:** A 2026-10-30-i tervben leírt hiányosság már nem áll fenn; a részletes, endpointonkénti jogosultsági szabályokat a backend kódja és a fenti aktuális összefoglaló tartalmazza.

### 10.4. ✅ `Felhasznalo.Role` enum típusának hiánya – kezelve

**Állapot (2026-11-19):** a `Role` továbbra is `string`, de a backend minden létrehozásnál és szerepkörváltásnál a megengedett listával (`RoleNames`) szemben ellenőriz, és a token-ellenőrzés is csak ismert szerepkört fogad el.


**Projektterv:** Négy jól definiált szerepkört határoz meg.  
**Kód:** A `Role` mező `string` típusú, semmilyen enum-megkötés nincs. Érvénytelen szerepkör is menthető lenne.  
**Ajánlás:** Backend validációban ellenőrizni kell a megengedett listával szemben. Opcionálisan konstans osztály vagy `[AllowedValues]` attribútum bevezetése javasolt.

### 10.5. ✅ `Foglalas` speciális igény mező – megoldva

**Állapot (2026-11-19):** a `Foglalas.SpecialisIgenyek` mező (legfeljebb 500 karakter) létezik, a foglalási űrlapon megadható.


**Projektterv:** „a foglaláshoz speciális igények (pl. labor, projektor) megadása"  
**Kód:** A `Foglalas` modellben nincs `SpekialisIgenyek` vagy hasonló mező.  
**Következmény:** Funkcionális hiányosság – ha demonstrálni kell, migrációt kell hozzáadni; ha nem prioritás, P2-re halasztható.

### 10.6. ✅ No-show automatizmus – megoldva

**Állapot (2026-11-19):** a `NoShowBackgroundService` percenként fut; a részletes szabályt az aktuális összefoglaló „Automatizmusok” része írja le.


**Projektterv:** „Ha a foglalás kezdő időpontjától számított konfigurálható időn belül nem történik kulcsfelvétel, a foglalás `NoShow` státuszt kap."  
**Kód:** A `Foglalas.Status` mező tartalmazza a `NoShow` értéket, de semmilyen háttérfolyamat vagy ütemező nem implementált.  
**Következmény:** Külön háttérszolgáltatás szükséges (pl. `IHostedService`), ami a 3. bemutatón P1/P2 prioritású.

---

*Az aktuális összefoglaló és a végpontlista a kódbázis 2026-11-19-i állapotát tükrözi; a 3–9. fejezet részletei a 2026-10-30-i tervből származnak.*
# SQL Server tranzakciós garanciák

A foglalás-átfedés ellenőrzése és a kulcs kiadásának állapotellenőrzése SQL
Serveren tranzakción belül történik. A backend SQL Server alkalmazászárral
sorozza ugyanazon terem foglalásmódosításait és ugyanazon kulcs kiadását vagy
visszavételét, ezért két párhuzamos kérés nem adhatja ki ugyanazt a kulcsot
kétszer, illetve nem kerülheti meg az átfedésvizsgálatot.

Az adatbázis foreign key-k és unique indexek a végponti ellenőrzésektől
függetlenül is kikényszerítik az integritást. A kulcsszám, felhasználónév,
e-mail-cím és mesterkulcs-jogosultság egyedi.
