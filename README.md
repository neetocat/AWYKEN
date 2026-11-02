# AWYKEN
AWYKEN is a student-focused mobile time management application designed to help college students manage their class schedules, commute time, and online classes more efficiently. The system allows students to scan their Certificate of Registration (COR), from which relevant information such as subjects, schedules, class days can be extracted.

## Cloud database (Azure SQL)

AWYKEN no longer keeps data in a local SQLite file. The MAUI app and the Blazor Web
app share one Azure SQL Database through EF Core:

* `AWYKEN.Shared/Data/AwykenDbContext.cs` – the `Profiles`, `Schedule`, and `Courses` tables.
* `AWYKEN.Shared/Data/SqlServerAwykenDataService.cs` – every read/write the pages use.
* `AWYKEN.Shared/Configuration/` – reads the Azure keys from `.env` for both hosts.
* `AWYKEN.Shared/Data/Migrations/` – the `InitialAzureSql` migration that creates the tables.

### Setup in three steps

1. Create an Azure SQL server and database, then allow your IP address in its firewall.
2. Copy the key template and fill in your values:

   ```powershell
   Copy-Item .env.example .env
   ```

   `.env` is Git-ignored, so the password never reaches the repository. The keys are
   `AZURE_SQL_SERVER`, `AZURE_SQL_DATABASE`, `AZURE_SQL_USER`, and `AZURE_SQL_PASSWORD`
   (or a single `AZURE_SQL_CONNECTION_STRING`).
3. Run the app. The first database call applies the migration automatically.

Full walkthrough, firewall rules, mobile `.env` locations, and troubleshooting:
[docs/azure-sql-setup.md](docs/azure-sql-setup.md).
