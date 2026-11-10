# SQL Server-adatbázis

Az adatbázis sémájának forrása az EF Core code-first modell és a backend
migrációs lánca. A jelenlegi, időrendi sorrend:

1. `20261020194917_InitialCreate`
2. `20261102105649_AddMasterKeyAccessUniqueConstraint`
3. `20261102133724_NormalizeRoleNames`
4. `20261102140952_AddAutomationIdempotencyIndexes`
5. `20261103105911_AddKeyMovementRecipient`
6. `20261103111859_AddAutomationIssueAuditKey`
7. `20261103141228_AddKeyNumberUniqueConstraint`
8. `20261109094123_AddSpecialNeedsAndRoomEquipment`
9. `20261109120001_AddKeyPinsAndMovementCondition`
10. `20261110164046_AddRecipientAwareKeyReturnNotifications`

Az 8. és 9. migráció a `Termek.Felszereltseg`,
`Foglalasok.SpecialisIgenyek`, `Felhasznalok.PinHash`,
`Kulcsmozgasok.TeremAllapot` és `Kulcsmozgasok.SerulesLeiras` mezőket adja
hozzá. Ezek SQL Serveren nullable oszlopok, ezért a korábbi rekordokban
`NULL` maradnak. A 10. migráció nullable foglalás-, címzett- és kulcsmozgás
hivatkozásokat, valamint nullable határidőt ad az értesítéshez; az új
`Olvasva` oszlop a meglévő értesítéseknél `false` alapértéket kap. A migráció
nem törli vagy alakítja át a meglévő értesítési rekordokat.

Az `InitialCreate` hozza létre többek között a felhasználók, termek, kulcsok,
foglalások, kulcsmozgások, értesítések, hibajegyek, karbantartási idősávok,
megbízhatósági események, auditnapló és rendszerparaméterek tábláit. A
`KulcsTerem` kapcsolótábla a kulcsok és termek közötti több-a-többhöz
kapcsolatot kezeli. Az értesítések foglaláshoz, címzett felhasználóhoz és
kulcsmozgáshoz is kapcsolódhatnak; az esti értesítés `AutomationKey` egyedi
indexe az adott kulcskiadáshoz és Portás címzetthez biztosít idempotenciát.
Az `Olvasva` mező az alkalmazáson belüli olvasottságot tárolja.

## Migrációk ellenőrzése és alkalmazása

Új migráció készítése után ellenőrizd a migrációs listát, a model snapshotot és
a generált SQL-t:

```bash
dotnet ef migrations list --project backend/backend.csproj
dotnet ef migrations has-pending-model-changes --project backend/backend.csproj
dotnet ef migrations script --project backend/backend.csproj --idempotent
```

Docker Compose-ban a backend `APPLY_MIGRATIONS=true` beállítással indul, és
indításkor `Database.Migrate()`-et hív. Megvárja az adatbázis healthcheckjét,
majd újrapróbálja a migrációkat, ha az SQL Server még nem érhető el:

```bash
docker compose up --build -d
docker compose ps
curl http://localhost:8000/api/health
```

Migráció előtt készíts mentést, és ellenőrizd annak érvényességét. Példa
Compose-ban futó, helyi SQL Serverhez (a célfájl neve legyen minden mentésnél
egyedi):

```bash
docker compose exec -T mssql sh -c \
  'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b' <<'SQL'
BACKUP DATABASE [KeyAndRoomDb]
  TO DISK = N'/var/opt/mssql/data/KeyAndRoomDb_pre_migration.bak'
  WITH COPY_ONLY, CHECKSUM;
RESTORE VERIFYONLY
  FROM DISK = N'/var/opt/mssql/data/KeyAndRoomDb_pre_migration.bak'
  WITH CHECKSUM;
GO
SQL
```

A mentés a perzisztens `mssql-data` volume-on marad; külső megőrzéshez másold
biztonságos helyre. Éles vagy megosztott adatbázison ne indítsd el az
automatikus migrációt külön jóváhagyás nélkül; ott előbb review-zd és
egyeztetett karbantartási eljárással futtasd a migrációs SQL-t.

## SQL-integrációs tesztek

A `SqlServerIntegrationTests` kizárólag dedikált, tesztelésre szánt SQL Server
kapcsolaton futtatható. A teszt egyedi nevű `ProjektLaborIntegration_<guid>`
adatbázisokat hoz létre, migrál, majd a teszt végén törli őket. **Ne állítsd a
`SQLSERVER_TEST_CONNECTION` változót éles vagy megőrzendő adatokat tartalmazó
adatbázisra.** A teszt connection stringje a teszt SQL Server `master`
adatbázisára mutasson, és rendelkezzen `CREATE DATABASE` jogosultsággal:

```bash
SQLSERVER_TEST_CONNECTION='Server=localhost,1433;Database=master;User Id=sa;Password=<teszt-jelszo>;TrustServerCertificate=True;Encrypt=False' \
  dotnet test ProjektLabor.sln --configuration Release \
  --filter 'FullyQualifiedName~SqlServerIntegrationTests'
```

A projekt `mssql-data` volume-ját ne töröld fejlesztői vagy tesztelési
adatbázis-frissítés céljából.
