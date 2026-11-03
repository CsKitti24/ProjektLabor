# SQL Server adatbázis

Az adatbázis sémáját kizárólag az EF Core migrációk kezelik. A jelenlegi lánc:

1. `InitialCreate`
2. `AddMasterKeyAccessUniqueConstraint`
3. `NormalizeRoleNames`
4. `AddKeyNumberUniqueConstraint`

Az `Ertesitesek` tábla és a foglaláshoz tartozó foreign key már az
`InitialCreate` migrációban szerepel. Új migrációt a backend projektből lehet
generálni, majd ellenőrizni kell a snapshotot és a tiszta SQL Serveren való
alkalmazást:

```bash
dotnet ef migrations list --project backend/backend.csproj
dotnet ef migrations script --project backend/backend.csproj --idempotent
```

Compose esetén az `APPLY_MIGRATIONS=true` indításkor alkalmazza a függő
migrációkat. A `mssql-data` volume-ot ne töröld tesztelés miatt; integrációs
tesztekhez külön adatbázist és külön connection stringet használj.
