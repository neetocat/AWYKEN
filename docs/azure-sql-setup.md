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

Admin-owned reference data (added with the Academic Management module) sits beside the
student tables and is never deleted, only archived:

| Table | EF entity | What a row represents |
| --- | --- | --- |
| `AcademicTerms` | `AcademicTermRecord` | One academic period (`2026-2027` + `1st`/`2nd`/`Summer`) with its dates. A filtered unique index allows exactly one current term. |
| `Subjects` | `SubjectRecord` | A catalog subject: unique code, title, units, college. |
| `ClassOfferings` | `ClassOfferingRecord` | One section of a subject in a term: section code, instructor, room, `Days` (`\|Mon\|Wed\|`), start/end time, and a Draft/Published/Cancelled status. |
| `CalendarFeeds` | `CalendarFeedRecord` | A public iCal (.ics) address the admin can import school-calendar entries from, with its last sync result. |
| `CalendarEvents` | `CalendarEventRecord` | A holiday, suspension, semester break, examination, or semester range. Term dates can be taken from a semester range. |

Reference rows have `IsArchived` instead of being deleted, and their foreign keys are
`NO ACTION` so a term or subject can never silently remove classes or student data.
`Schedule.TermId` and `Schedule.ClassOfferingId` are nullable links: rows imported from a
COR start unlinked and are claimed by the admin's "Link" action, which keeps their ids so
course notes and incentives survive.

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
| `AWYKEN_ADMIN_EMAILS` | – | Comma-separated emails that get the admin role. See [Admin account](#9-admin-account). |
| `AWYKEN_DEFAULT_ADMIN_EMAIL` | – | Optional shared admin, created on first start. Must be set together with the password. |
| `AWYKEN_DEFAULT_ADMIN_PASSWORD` | – | Password for the shared admin; hashed before storage, never read back. |
| `AWYKEN_DEFAULT_ADMIN_NAME` | `Administrator` | Display name for the shared admin. |

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
| `CREATE TABLE permission denied in database ...` while starting | The SQL login may read and write but not change the schema. Grant `db_owner` (or apply migrations yourself and set `AWYKEN_APPLY_MIGRATIONS_ON_START=False`). |
| `The INSERT permission was denied on the object ...` / `The UPDATE permission was denied ...` | The login is read-only. Grant `db_datawriter`, or `db_owner` for a dev database. |
| `The SELECT permission was denied ...` | The login exists on the server but has no user in this database. Run `CREATE USER ... FOR LOGIN ...` in the `awyken` database. |
| `User must be in the master database` (error 916) | `CREATE LOGIN` only runs in `master`. Either create a contained user (`CREATE USER ... WITH PASSWORD`) in `awyken`, or use SSMS/sqlcmd connected to `master`. |
| `USE statement is not supported to switch between databases` | The portal Query editor is pinned to one database. Use a contained user, or a client that can connect to `master`. |
| `AWYKEN_DEFAULT_ADMIN_EMAIL and AWYKEN_DEFAULT_ADMIN_PASSWORD must be set together` | One of the two shared-admin keys is empty. Set both, or remove both to disable seeding. |
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

Prefer one account per person: it is auditable, and one person can be removed without
changing everybody's password.

**Option A — contained user (works in the portal Query editor)**

The portal editor is pinned to the database you opened and rejects `USE`, so create a
database-contained user. Run this in the **awyken** Query editor:

```sql
CREATE USER awyken_<name> WITH PASSWORD = '<strong password>';
ALTER ROLE db_owner ADD MEMBER awyken_<name>;   -- dev database only: migrations need DDL rights
```

No `master` access is needed, and the app connects straight to `awyken`, so contained
authentication is enough. The catch: a contained user exists only in that database.

**Option B — server-level login**

Use SSMS, Azure Data Studio, or `sqlcmd` with the database set to `master`:

```sql
CREATE LOGIN awyken_<name> WITH PASSWORD = '<strong password>';
```

then reconnect with the database set to `awyken`:

```sql
CREATE USER awyken_<name> FOR LOGIN awyken_<name>;
ALTER ROLE db_owner ADD MEMBER awyken_<name>;
```

> `master` is the server's system database and is not listed in the portal Explorer tree,
> which only shows the selected database. The portal editor also refuses to switch context
> (*"USE statement is not supported to switch between databases"*), which is why Option A is
> the shorter path there.

Least privilege alternative for a shared or demo database: the owner applies migrations
once, teammates get `ALTER ROLE db_datareader ADD MEMBER ...` and
`ALTER ROLE db_datawriter ADD MEMBER ...`, and each teammate sets
`AWYKEN_APPLY_MIGRATIONS_ON_START=False` in their `.env`. Pick one path — `db_owner`
already implies both data roles.

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

---

## 9. Admin account

The app has one extra role: **admin**. It is a boolean column on `Profiles`, not a separate
table. It gates the administration dashboard at `/admin`, which is part of the **Web** app
(`AWYKEN.Web/Components/Pages/Admin/Admin.razor`). The MAUI app has no admin screen, so
admins there behave like normal users.

### Grant the role

Put the login email in `.env`:

```ini
AWYKEN_ADMIN_EMAILS=you@example.com,other-admin@example.com
```

Restart the app, then sign in (or sign up) with that address. `SignInAsync` /
`SignUpAsync` see the match and set `Profiles.IsAdmin = 1`; on the Web host `Settings.razor`
then redirects you to `/admin` instead of showing "You are logged in", and the settings page
shows an ADMIN row.

Only someone who can edit the server's `.env` can grant admin — there is no self-service
promotion and no admin password to leak. A signed-in non-admin who types `/admin` is sent
back to `/settings` by the server-side guard at the top of `Admin.razor`.

### Shared default admin

For a demo where everyone should be able to open the dashboard with the same credentials,
set both keys in the `.env` of the machine that runs the app first:

```ini
AWYKEN_DEFAULT_ADMIN_EMAIL=admin@awyken.local
AWYKEN_DEFAULT_ADMIN_PASSWORD=Awyk3n-Shared-Admin-2026!
AWYKEN_DEFAULT_ADMIN_NAME=Administrator
```

On the next start the app creates that account with `IsAdmin = 1` if it is missing, and
promotes it if it exists without the flag. It never rewrites an existing password. Sign in
with those credentials on the Web app and you land on `/admin` like any other administrator.

The keys are only needed once — after the row exists in the database, every machine and
every teammate can use the same login. Beware that this is a shared credential: anyone who
has it can change or delete the whole timetable, so rotate or remove it after the demo.

While the keys are set, a missing `IsAdmin` flag is re-applied on every start. To change
the password, delete the account row and restart with the new value (the app recreates it
with the new password). To remove access, drop the keys **and** then:

```sql
UPDATE [Profiles] SET [IsAdmin] = 0 WHERE [Email] = 'admin@awyken.local';
-- or delete it entirely (cascades to its schedule, notes, and incentives):
DELETE FROM [Profiles] WHERE [Email] = 'admin@awyken.local';
```

### Revoke the role

Remove the address from `.env`, then clear the flag once (the app never demotes anyone
automatically):

```sql
UPDATE [Profiles] SET [IsAdmin] = 0 WHERE [Email] = 'you@example.com';
```

### Notes

* `IsAdmin` defaults to `0`, so the migration cannot promote existing accounts.
* Multiple admins are fine; each address just needs to be listed.
* The page reads the database directly with the same login as the rest of the app, so no
  extra database permissions are needed for admins.
