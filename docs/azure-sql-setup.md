# Azure SQL Database setup

AWYKEN used to store everything in a per-installation SQLite file
(`awyken.db` in the app-data folder). Both hosts — the .NET MAUI app and the
Blazor Web app — now talk to one shared **Azure SQL Database** through EF Core.

Nothing about the screens changed: the pages still inject `IAwykenDataService`,
which is now implemented by `SqlServerAwykenDataService`.

| Before | After |
| --- | --- |
| `Microsoft.EntityFrameworkCore.Sqlite` | `Microsoft.EntityFrameworkCore.SqlServer` |
| `SqliteAwykenDataService` | `SqlServerAwykenDataService` |
| `options.UseSqlite("Data Source=...\awyken.db")` | `options.UseSqlServer(<connection from .env>)` |
| SQLite migrations (`InitialSqlite`, ...) | One `InitialAzureSql` SQL Server migration |
| Secrets: none | Secrets: `.env` (Git-ignored), `.env.example` documents the keys |

---

## 1. What data moves to the cloud

The EF model is unchanged, so the same three tables and the same rows move:

| Table | EF entity | What a row represents |
| --- | --- | --- |
| `Profiles` | `UserProfileRecord` | One account (or the anonymous **Guest** row with a `NULL` email) plus the student details parsed from a COR and the saved commute endpoints. |
| `Schedule` | `ScheduleRecord` | One class: start/end, course code, title, room or link, instructor, mode, colour, units, `Days` (`\|Mon\|Wed\|` for recurring classes, `NULL` for a one-time class), attendance status. |
| `Courses` | `CourseNoteRecord` | One note for **one class on one date**, including news, attendance, and an optional image (`varbinary(max)`), unique per `(ProfileId, ScheduleRecordId, ClassDate)`. |

Every row is scoped by `ProfileId`; `Schedule` and `Courses` cascade-delete with their
profile. The SQL Server migration adds the same indexes the SQLite schema had, including
the filtered unique index `IX_Profiles_Email` (`WHERE [Email] IS NOT NULL`) so the Guest
row can coexist with real accounts.

Migrations are applied automatically on first use (`AWYKEN_APPLY_MIGRATIONS_ON_START=True`,
the default). The old `PRAGMA table_info` repair code is gone because the SQL Server
migration creates `AcademicTerm` correctly.

---

## 2. Create the server and database

Using the Azure CLI:

```bash
az sql server create \
  --resource-group AWYKEN \
  --name awyken-sql \
  --location southeastasia \
  --admin-user awykenadmin \
  --admin-password "<a strong password>"

# A cheap tier is fine for a student project; General Purpose serverless scales to zero.
az sql db create \
  --resource-group AWYKEN \
  --server awyken-sql \
  --name awyken \
  --service-objective GP_S_Gen5_2 \
  --backup-storage-redundancy Local
```

Or in the portal: **Create a resource → Azure SQL → Create** (Database + server), then
choose your subscription, resource group, server name, SQL authentication, and an
"Allow Azure services" networking option for testing.

Your server address is `awyken-sql.database.windows.net` and the port is `1433`.

### Firewall

Azure SQL refuses connections from unknown IP addresses, which matters for a mobile app
that changes networks:

```bash
# The machine running the Web host / MAUI emulator:
az sql server firewall-rule create -g AWYKEN -s awyken-sql \
  -n MyLaptop --start-ip-address <your public IP> --end-ip-address <your public IP>

# Temporary: allow all Azure-hosted resources (App Service, Container Apps, ...)
az sql server firewall-rule create -g AWYKEN -s awyken-sql \
  -n AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

A phone on mobile data has its own public IP, so either add that IP, or (recommended for
anything beyond a demo) keep the database private and put the existing `AWYKEN.Web` host
in front of it as an API.

### Database user for the app

The server admin login works, but a scoped login is better. Connect to the `awyken`
database with the admin account and run:

```sql
CREATE LOGIN awyken_app WITH PASSWORD = '<another strong password>';
CREATE USER awyken_app FOR LOGIN awyken_app;
ALTER ROLE db_owner ADD MEMBER awyken_app;   -- needed only while the app applies migrations
```

For production: run `dotnet ef database update` once with a privileged account, set
`AWYKEN_APPLY_MIGRATIONS_ON_START=False`, and give the app `db_datareader` +
`db_datawriter` instead of `db_owner`.

---

## 3. Fill in `.env`

```powershell
Copy-Item .env.example .env      # PowerShell
cp .env.example .env             # bash
```

Then set either the full connection string:

```ini
AZURE_SQL_CONNECTION_STRING=Server=tcp:awyken-sql.database.windows.net,1433;Initial Catalog=awyken;User ID=awyken_app;Password=<password>;Encrypt=True;TrustServerCertificate=False;
```

or the discrete keys (simplest to edit):

```ini
AZURE_SQL_SERVER=awyken-sql.database.windows.net
AZURE_SQL_DATABASE=awyken
AZURE_SQL_USER=awyken_app
AZURE_SQL_PASSWORD=<password>
```

`.env` is listed in `.gitignore`; `.env.example` is committed. Both hosts read it:

* **Web:** `AWYKEN.Web/Program.cs` → `AwykenDatabaseSettings.Load(key => builder.Configuration[key])`
  (`appsettings.json`, user secrets, and environment variables are also read, and take
  precedence over `.env`).
* **MAUI:** `MauiProgram.cs` → `AwykenDatabaseSettings.Load(...)` with
  `FileSystem.AppDataDirectory` as an extra search folder.

Lookup order, first non-empty value wins: host configuration → `.env` file → packaged
`.env` (mobile) → process environment variables.

### Supported keys

| Key | Default | Purpose |
| --- | --- | --- |
| `AZURE_SQL_CONNECTION_STRING` | – | Full ADO.NET string; wins over the discrete keys. |
| `AZURE_SQL_SERVER` | – | `server.database.windows.net` (no `tcp:`, no `,1433`). |
| `AZURE_SQL_DATABASE` | – | Database name. |
| `AZURE_SQL_USER` | – | SQL login, or the client id for a service principal. |
| `AZURE_SQL_PASSWORD` | – | Login password (quote it if it contains `#` or spaces). |
| `AZURE_SQL_AUTHENTICATION` | – | `Active Directory Managed Identity`, `Active Directory Service Principal`, `Active Directory Password`, `Active Directory Default`. |
| `AZURE_SQL_ENCRYPT` | `True` | Keep `True`; Azure SQL requires TLS. |
| `AZURE_SQL_TRUST_SERVER_CERTIFICATE` | `False` | Only for a local test server with a self-signed certificate. |
| `AZURE_SQL_CONNECT_TIMEOUT` | `30` | Seconds to wait for a connection. |
| `AZURE_SQL_COMMAND_TIMEOUT` | `60` | Seconds EF Core allows per command. |
| `AWYKEN_APPLY_MIGRATIONS_ON_START` | `True` | Apply migrations on first database access. |

Missing values fail fast with a message naming the keys and the `.env` file that was
read, rather than silently falling back to a local database.

---

## 4. Create the tables

The app applies migrations automatically. To create them ahead of time:

```powershell
cd AWYKEN\AWYKEN          # folder that contains AWYKEN.slnx
dotnet tool restore       # installs dotnet-ef 10.0.13
dotnet ef database update -p AWYKEN.Shared -s AWYKEN.Web
```

If the local tool manifest is blocked by Windows (`... came from another computer and
might be blocked`), unblock it first:

```powershell
Unblock-File .config\dotnet-tools.json
```

`dotnet ef` uses `AwykenDesignTimeDbContextFactory`, which reads the same `.env` values.
`migrations add` does not open a connection, so it also works on a fresh clone where
`.env` is still empty.

To add a future schema change:

```powershell
dotnet ef migrations add AddSomething -p AWYKEN.Shared -s AWYKEN.Web -o Data/Migrations
dotnet ef database update -p AWYKEN.Shared -s AWYKEN.Web
```

> The old SQLite migrations were deleted. A SQLite `__EFMigrationsHistory` cannot be
> reused by SQL Server, so the cloud database starts from `InitialAzureSql`. Nothing from
> the device-local `awyken.db` is uploaded automatically.

---

## 5. Running

**Web**

```powershell
dotnet run --project AWYKEN\AWYKEN\AWYKEN.Web
```

The console prints the resolved target, for example:

```
AWYKEN database: awyken on awyken-sql.database.windows.net (settings from C:\...\AWYKEN\.env).
```

**MAUI on Windows** — the repository `.env` is found by walking up from the build output:

```powershell
dotnet build -f net10.0-windows10.0.19041.0 -t:Run AWYKEN\AWYKEN\AWYKEN\AWYKEN.csproj
```

**MAUI on Android / iOS emulator or phone** — the app cannot see the repository, so use
one of these:

1. Push a `.env` into the app's private data folder (debug builds):

   ```bash
   adb push .env /data/local/tmp/.env
   adb shell run-as com.companyname.awyken cp /data/local/tmp/.env files/.env
   ```

   `FileSystem.AppDataDirectory` maps to `files/` for that package.

2. Or place `.env` in `AWYKEN\AWYKEN\AWYKEN\Resources\Raw\.env` and rebuild; it is
   packaged as a raw asset and read at startup. `.gitignore` covers this path too, but
   remember the keys then live inside the app package.

---

## 6. Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `A login failed for user ...` (error 18456) | Wrong `AZURE_SQL_USER` / `AZURE_SQL_PASSWORD`, or the login was created on `master` without a database user. |
| `Cannot open server ... requested by the login` (40615) | The client IP is not in the server firewall. |
| `A network-related or instance-specific error` | Wrong `AZURE_SQL_SERVER`, or port 1433 blocked by the network. |
| `The certificate chain was issued by an authority that is not trusted` | Local SQL Server only: set `AZURE_SQL_TRUST_SERVER_CERTIFICATE=True`. Never for Azure SQL. |
| `AWYKEN could not resolve the Azure SQL Database connection` | `.env` missing/empty; the message lists the keys it needs. |
| Startup is blocked by an exception on a page | Cloud database unreachable. The wrapped error names the server and database. |

---

## 7. Security note

A connection string embedded in a client app can be extracted from the binary, so a
mobile app holding a database password is acceptable for a school project or an internal
demo, not for a public release. The usual next step is to keep Azure SQL private and let
`AWYKEN.Web` expose the same `IAwykenDataService` operations as HTTP endpoints, then point
the MAUI app at that API. The data layer was kept behind `IAwykenDataService` precisely so
that swap only touches the service implementation and the two host registrations.
