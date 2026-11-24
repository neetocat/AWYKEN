# Mobile API and database credentials

The MAUI app no longer opens Azure SQL. It calls the existing `AWYKEN.Web` host over HTTPS at `/api/v1`.
The Web host is the only application that receives Azure SQL credentials. Mobile accounts receive an
8-hour bearer token after sign-in; the token is stored with MAUI `SecureStorage` and is checked against
the account status on each API request. A disabled, suspended, or deactivated account cannot keep using
an old token.

## Configure and deploy the server

1. Deploy `AWYKEN.Web` to an HTTPS host.
2. Set `AWYKEN_API_TOKEN_SECRET` in the host's environment or application settings to a random value
   at least 32 bytes long. Keep it on the server. Rotating it signs out all mobile sessions.
3. Put the Azure SQL settings in the server's configuration. For App Service, prefer a system-assigned
   managed identity and `AZURE_SQL_AUTHENTICATION=Active Directory Managed Identity`; grant that
   identity only the required database roles. The SQL server password is not needed in this mode.
4. Restrict Azure SQL networking to the Web host's outbound IP addresses or a private endpoint. The
   mobile devices no longer need SQL firewall access.

## Configure the mobile build

Set `AWYKEN/Resources/Raw/api-base-url.txt` to the deployed host origin, for example
`https://awyken.example.edu/`, then rebuild the Android app. This URL is public configuration, not a
secret. Do not put a database connection string, SQL password, API signing key, or administrator
password in MAUI assets, app settings, or build properties.

The API supports public school-calendar reads for students and guests. Account data is scoped to the
profile represented by the bearer token. Admin-only data operations still run through the existing
server-side admin checks. Public account registration always creates a non-admin account; an existing
administrator must grant administrator access from the admin page.

## Rotate credentials previously shipped in an APK

Any earlier Debug APK that included `.env` can be inspected. After the server is deployed and the new
mobile build is in use, rotate the Azure SQL password and any default administrator password that was
included in a packaged `.env`. Remove or uninstall old APKs where practical.
