# Distopia Network — Data Access Layer

## File inclusi in questo archivio

Questi file si aggiungono (o sostituiscono) al progetto esistente.
Copiare rispettando la struttura di cartelle.

---

## DistopiaNetwork.Server — File da aggiungere/sostituire

| File | Azione |
|------|--------|
| `Entities/PodcastEntity.cs` | NUOVO |
| `Entities/CacheEntryEntity.cs` | NUOVO |
| `Data/AppDbContext.cs` | NUOVO |
| `Data/Repositories/IRepository.cs` | NUOVO |
| `Data/Repositories/IPodcastRepository.cs` | NUOVO |
| `Data/Repositories/PodcastRepository.cs` | NUOVO |
| `Data/Repositories/ICacheEntryRepository.cs` | NUOVO |
| `Data/Repositories/CacheEntryRepository.cs` | NUOVO |
| `Data/UnitOfWork/IUnitOfWork.cs` | NUOVO |
| `Data/UnitOfWork/UnitOfWork.cs` | NUOVO |
| `Data/Migrations/20260310000001_InitialCreate.cs` | NUOVO |
| `Data/Migrations/AppDbContextModelSnapshot.cs` | NUOVO |
| `Services/CatalogService.cs` | SOSTITUISCE quello esistente |
| `Services/CacheService.cs` | SOSTITUISCE quello esistente |
| `Services/SyncService.cs` | SOSTITUISCE quello esistente |
| `Services/CacheCleanupService.cs` | SOSTITUISCE quello esistente |
| `Program.cs` | SOSTITUISCE quello esistente |
| `appsettings.json` | AGGIORNA la connection string |

## DistopiaNetwork.PublisherClient — File da aggiungere/sostituire

| File | Azione |
|------|--------|
| `Entities/LocalEpisodeEntity.cs` | NUOVO |
| `Data/PublisherDbContext.cs` | NUOVO |
| `Data/Repositories/ILocalEpisodeRepository.cs` | NUOVO |
| `Data/Repositories/LocalEpisodeRepository.cs` | NUOVO |
| `Data/Migrations/20260310000001_InitialCreate.cs` | NUOVO |
| `Data/Migrations/PublisherDbContextModelSnapshot.cs` | NUOVO |
| `Services/PublishService.cs` | SOSTITUISCE quello esistente |
| `Program.cs` | SOSTITUISCE quello esistente |

---

## NuGet packages da installare

```bash
# Server
dotnet add DistopiaNetwork.Server package Microsoft.EntityFrameworkCore.SqlServer --version 9.*
dotnet add DistopiaNetwork.Server package Microsoft.EntityFrameworkCore.Tools --version 9.*
dotnet add DistopiaNetwork.Server package Microsoft.EntityFrameworkCore.Design --version 9.*

# PublisherClient
dotnet add DistopiaNetwork.PublisherClient package Microsoft.EntityFrameworkCore.Sqlite --version 9.*
dotnet add DistopiaNetwork.PublisherClient package Microsoft.EntityFrameworkCore.Tools --version 9.*
```

---

## Connection string SQL Server (appsettings.json)

Aggiornare in `DistopiaNetwork.Server/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=DistopiaNetwork;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

Per Docker/Azure sostituire con la propria connection string.

---

## Migrazioni

Le migrazioni sono già incluse nella cartella `Data/Migrations/` di ogni progetto.
Vengono applicate automaticamente all'avvio tramite `db.Database.MigrateAsync()`.

Per generare migrazioni future dopo modifiche alle Entity:

```bash
# Server
cd DistopiaNetwork.Server
dotnet ef migrations add NomeMigrazione --context AppDbContext

# PublisherClient  
cd DistopiaNetwork.PublisherClient
dotnet ef migrations add NomeMigrazione --context PublisherDbContext
```

---

## Note importanti

- **ServerSettings.Section**: verificare che questa costante esista in `Configuration/ServerSettings.cs`
- **PublisherSettings.Section**: verificare che questa costante esista in `Configuration/PublisherSettings.cs`
- **CryptoHelper**: i metodi `VerifyMetadata`, `ComputeFileHash`, `SignMetadata` devono esistere in `DistopiaNetwork.Shared/Crypto/CryptoHelper.cs`
- **Namespace**: adattare i namespace se il progetto usa convenzioni diverse
