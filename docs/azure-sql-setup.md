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
| SQLite migrations (`InitialSqlite`, ...) | SQL Server migrations: `InitialAzureSql`, then `AddTeacherPinAndStudentIncentives` |
| Secrets: none | Secrets: `.env` (Git-ignored), `.env.example` documents the keys |

---

## 1. What data moves to the cloud

The EF model is unchanged, so the same tables and the same rows move:

| Table | EF entity | What a row represents |
| --- | --- | --- |
| `Profiles` | `UserProfileRecord` | One account (or the anonymous **Guest** row with a `NULL` email) plus the student details parsed from a COR, the teacher PIN hash, and the saved commute endpoints. |
| `Schedule` | `ScheduleRecord` | One class: start/end, course code, title, room or link, instructor, mode, colour, units, `Days` (`\|Mon\|Wed\|` for recurring classes, `NULL` for a one-time class), attendance status. |
| `Courses` | `CourseNoteRecord` | One note for **one class on one date**, including news, attendance, and an optional image (`varbinary(max)`), unique per `(ProfileId, ScheduleRecordId, ClassDate)`. |
| `StudentIncentives` | `StudentIncentiveRecord` | One teacher-awarded incentive: points, reason, and the class it belongs to. |

Every row is scoped by `ProfileId`. Deletes cascade down a single chain: `Profiles` →
`Schedule` → `Courses` / `StudentIncentives`. `Courses.ProfileId` and
`StudentIncentives.ProfileId` are deliberately `NO ACTION` because SQL Server rejects a
table that can be reached by two cascade paths (error 1785), even though SQLite allowed
it. Deleting a class still deletes its notes and incentives, and deleting a profile still
removes everything through the `Schedule` rows.

The SQL Server migration adds the same indexes the SQLite schema had, including the
filtered unique index `IX_Profiles_Email` (`WHERE [Email] IS NOT NULL`) so the Guest row
can coexist with real accounts.

Migrations are applied automatically on first use (`AWYKEN_APPLY_MIGRATIONS_ON_START=True`,
the default). The old `PRAGMA table_info` repair code is gone because the SQL Server
migration creates `AcademicTerm` correctly.

---

## 2. Create the server and database

Using the Azure CLI (single-line commands: they work in both PowerShell and bash; `\`
line continuations are bash-only and break in PowerShell):

```bash
az sql server create --resource-group AWYKEN --name awyken-sql --location southeastasia --admin-user awykenadmin --admin-password "<a strong password>"

# A cheap tier is fine for a student project; General Purpose serverless scales to zero.
az sql db create --resource-group AWYKEN --server awyken-sql --name awyken --service-objective GP_S_Gen5_2 --backup-storage-redundancy Local
```

Or in the portal: **Create a resource → Azure SQL → Create** (Database + server), then
choose your subscription, resource group, server name, SQL authentication, and an
"Allow Azure services" networking option for testing.

Your server address is `awyken-sql.database.windows.net` and the port is `1433`.

### Firewall

Azure SQL refuses connections from unknown IP addresses, which matters for a mobile app
that changes networks:

```bash
# The machine running the Web host / MAUI emulator (public IP from https://ifconfig.me):
az sql server firewall-rule create -g AWYKEN -s awyken-sql -n MyLaptop --start-ip-address <your public IP> --end-ip-address <your public IP>

# Temporary: allow all Azure-hosted resources (App Service, Container Apps, ...)
az sql server firewall-rule create -g AWYKEN -s awyken-sql -n AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Confirm what is allowed:
az sql server firewall-rule list -g AWYKEN -s awyken-sql -o table
```

Get your public IPv4 with `curl -4 -s https://ifconfig.me` — on a dual-stack connection
the plain address can come back as IPv6, which the command above rejects with
*"IP address must be a valid IPv4 address"*.

Some ISPs hand out IPv6 and Azure SQL has a separate rule set for it:

```bash
az sql server ipv6-firewall-rule create -g AWYKEN -s awyken-sql -n MyLaptop-v6 --start-ipv6-address <your IPv6> --end-ipv6-address <your IPv6>
az sql server ipv6-firewall-rule list -g AWYKEN -s awyken-sql -o table
```

This only works if the gateway answers over IPv6 for your network; check with
`Resolve-DnsName awyken-sql.database.windows.net -Type AAAA` first. No AAAA record means
you need the IPv4 rule instead.

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

### If a first migration attempt failed

A migration runs in one transaction, so a failure should leave nothing behind. If a
previous run did leave tables (for example after an error 1785 while `Courses` was being
created), clear them and let the app recreate the schema:

```sql
DROP TABLE IF EXISTS [Courses];
DROP TABLE IF EXISTS [Schedule];
DROP TABLE IF EXISTS [Profiles];
DROP TABLE IF EXISTS [__EFMigrationsHistory];
```

Check what is left with `SELECT name FROM sys.tables ORDER BY name;` and
`SELECT * FROM [__EFMigrationsHistory];` in the Azure portal Query editor or SSMS.

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
| `Introducing FOREIGN KEY constraint ... may cause cycles or multiple cascade paths` (error 1785) | A table was reachable by two `ON DELETE CASCADE` paths. Fixed in the model: `Courses.ProfileId` is `NO ACTION`. Drop any half-created tables (see "If a first migration attempt failed") and restart. |
| `There is already an object named 'Profiles' in the database` | A previous failed attempt left tables behind. Run the drop script in section 4. |
| Startup is blocked by an exception on a page | Cloud database unreachable. The wrapped error names the server and database. |

---

## 7. Security note

A connection string embedded in a client app can be extracted from the binary, so a
mobile app holding a database password is acceptable for a school project or an internal
demo, not for a public release. The usual next step is to keep Azure SQL private and let
`AWYKEN.Web` expose the same `IAwykenDataService` operations as HTTP endpoints, then point
the MAUI app at that API. The data layer was kept behind `IAwykenDataService` precisely so
that swap only touches the service implementation and the two host registrations.

---

## 8. Teammates joining the project

Only **one** person needs an Azure account. Everyone else connects to that server with
credentials the owner gives them out of band.

### What the owner shares

Send **server name, database name, login, password** through a password manager share link
or a private message — never in the Git repository, a PR description, a public channel, or
a screenshot. `.env` is in `.gitignore`, and `.env.example` (the committed template) only
contains placeholders.

### 1. Allow each teammate's network

Azure SQL blocks unknown IP addresses (error 40615). Each teammate runs

```bash
curl ifconfig.me        # or open https://ifconfig.me in a browser
```

and sends the owner the address, who adds it:

```bash
# One line: works in PowerShell and bash alike.
az sql server firewall-rule create -g AWYKEN -s awyken-sql -n teammate-<name> --start-ip-address <their IP> --end-ip-address <their IP>
```

Home and mobile IPs change, so expect to re-add a rule now and then. A phone on mobile data
has a different address from the same phone on Wi-Fi.

### 2. Give each teammate their own login

Prefer one login per person: it is auditable, and one person can be removed without
changing everybody's password. Run on the **master** database:

```sql
CREATE LOGIN awyken_<name> WITH PASSWORD = '<strong password>';
```

then on the **awyken** database:

```sql
CREATE USER awyken_<name> FOR LOGIN awyken_<name>;
ALTER ROLE db_owner ADD MEMBER awyken_<name>;   -- dev database only: migrations need DDL rights
```

Least privilege alternative for a shared or demo database: the owner applies migrations
once, teammates get `ALTER ROLE db_datareader ADD MEMBER ...` and
`ALTER ROLE db_datawriter ADD MEMBER ...`, and each teammate sets
`AWYKEN_APPLY_MIGRATIONS_ON_START=False` in their `.env`.

### 3. Teammate setup

```powershell
git fetch origin
git switch <branch>
Copy-Item .env.example .env
```

Fill `.env` in the **repository root** with the shared values:

```ini
AZURE_SQL_SERVER=awyken-sql.database.windows.net
AZURE_SQL_DATABASE=awyken
AZURE_SQL_USER=awyken_<name>
AZURE_SQL_PASSWORD=<their own password>
```

Then:

```powershell
dotnet run --project AWYKEN\AWYKEN\AWYKEN.Web
```

The console prints `AWYKEN database: awyken on awyken-sql.database.windows.net`, and the
first page load applies any pending migration.

### 4. Warn them: the database is shared

* **Sign up for an account inside the app** (Settings → Create account). The anonymous
  **Guest** profile is a single row, so two people in Guest mode see and edit the *same*
  schedule. A real account gets its own `ProfileId`, and every query is scoped to it.
* Data is no longer per device: a class added on one laptop appears on everyone's app.
  Keep experiments out of the data you will demo, or use a second database
  (`awyken-dev`) on the same server for messing around.
* Migrations are safe to race: EF takes an `sp_getapplock` lock (visible in the log), so
  two people starting the app at the same time will not corrupt the schema.

### 5. Optional: no Azure account at all

The code only needs a SQL Server. A teammate can run one locally in Docker and point
`.env` at it — no Azure, no firewall rules:

```powershell
docker run -d --name awyken-sql -e "ACCEPT_EULA=Y" `
  -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" -p 1433:1433 `
  mcr.microsoft.com/mssql/server:2022-latest
```

```ini
AZURE_SQL_SERVER=localhost,1433
AZURE_SQL_DATABASE=awyken
AZURE_SQL_USER=sa
AZURE_SQL_PASSWORD=YourStrong!Passw0rd
AZURE_SQL_TRUST_SERVER_CERTIFICATE=True
```

`MigrateAsync` creates the `awyken` database if it does not exist. This is also the
cheapest way to try schema changes without touching the shared cloud database.

### 6. Housekeeping

* After the project: `DROP LOGIN` for each teammate, rotate the admin password, and do not
  publish the `.env` contents or the live server name in a portfolio write-up.
* If a laptop with `.env` is lost, rotate that person's SQL login — everyone else keeps
  working.
