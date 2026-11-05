using System.Globalization;
using Microsoft.Data.SqlClient;

namespace AWYKEN.Shared.Configuration;

/// <summary>
/// Resolves the Azure SQL Database connection for both hosts.
/// <para>
/// Values are looked up in this order and the first non-empty value wins:
/// host configuration (appsettings, user secrets, environment variables),
/// the repository <c>.env</c> file, values supplied by the host (such as a
/// packaged <c>.env</c> on mobile), then plain process environment variables.
/// </para>
/// </summary>
public sealed record AwykenDatabaseSettings
{
    public const string ConnectionStringKey = "AZURE_SQL_CONNECTION_STRING";
    public const string ServerKey = "AZURE_SQL_SERVER";
    public const string DatabaseKey = "AZURE_SQL_DATABASE";
    public const string UserKey = "AZURE_SQL_USER";
    public const string PasswordKey = "AZURE_SQL_PASSWORD";
    public const string AuthenticationKey = "AZURE_SQL_AUTHENTICATION";
    public const string EncryptKey = "AZURE_SQL_ENCRYPT";
    public const string TrustServerCertificateKey = "AZURE_SQL_TRUST_SERVER_CERTIFICATE";
    public const string ConnectTimeoutKey = "AZURE_SQL_CONNECT_TIMEOUT";
    public const string CommandTimeoutKey = "AZURE_SQL_COMMAND_TIMEOUT";
    public const string ApplyMigrationsKey = "AWYKEN_APPLY_MIGRATIONS_ON_START";
    // Comma-separated list of emails that are promoted to administrator on sign-in.
    public const string AdminEmailsKey = "AWYKEN_ADMIN_EMAILS";
    // Optional shared administrator that the app creates on startup if it does not exist.
    public const string DefaultAdminEmailKey = "AWYKEN_DEFAULT_ADMIN_EMAIL";
    public const string DefaultAdminPasswordKey = "AWYKEN_DEFAULT_ADMIN_PASSWORD";
    public const string DefaultAdminNameKey = "AWYKEN_DEFAULT_ADMIN_NAME";

    /// <summary>The ADO.NET connection string handed to EF Core and Microsoft.Data.SqlClient.</summary>
    public required string ConnectionString { get; init; }

    /// <summary>Server name from the connection string, safe to log (never the password).</summary>
    public required string DataSource { get; init; }

    /// <summary>Database name from the connection string, safe to log.</summary>
    public required string InitialCatalog { get; init; }

    /// <summary>
    /// Emails that become administrators when they sign in, read from
    /// <c>AWYKEN_ADMIN_EMAILS</c>. Only someone who can edit the server's <c>.env</c>
    /// can grant the admin role, and no manual SQL is needed.
    /// </summary>
    public IReadOnlyList<string> AdminEmails { get; init; } = [];

    /// <summary>
    /// A shared administrator account seeded on startup when
    /// <c>AWYKEN_DEFAULT_ADMIN_EMAIL</c> and <c>AWYKEN_DEFAULT_ADMIN_PASSWORD</c> are set.
    /// The account is created only when missing; an existing one keeps its password.
    /// </summary>
    public string? DefaultAdminEmail { get; init; }

    /// <summary>Password for <see cref="DefaultAdminEmail"/>; hashed before it is stored.</summary>
    public string? DefaultAdminPassword { get; init; }

    /// <summary>Display name for the seeded administrator; defaults to "Administrator".</summary>
    public string? DefaultAdminName { get; init; }

    /// <summary>True when a shared default admin should be seeded.</summary>
    public bool HasDefaultAdmin =>
        !string.IsNullOrWhiteSpace(DefaultAdminEmail) && !string.IsNullOrWhiteSpace(DefaultAdminPassword);

    /// <summary>Seconds EF Core allows a single command to run.</summary>
    public int CommandTimeoutSeconds { get; init; } = 60;

    /// <summary>When true the first database access applies pending EF Core migrations.</summary>
    public bool ApplyMigrationsOnStartup { get; init; } = true;

    /// <summary>The <c>.env</c> file that was read, or <c>null</c> when none was found.</summary>
    public string? DotEnvFile { get; init; }

    /// <summary>
    /// Builds the settings, or throws <see cref="AwykenDatabaseConfigurationException"/>
    /// with instructions when no usable connection can be resolved.
    /// </summary>
    /// <param name="hostConfiguration">Lookup such as <c>key =&gt; builder.Configuration[key]</c>.</param>
    /// <param name="extraValues">Additional key/value pairs, e.g. a packaged <c>.env</c>.</param>
    /// <param name="additionalRoots">Extra folders to search for a <c>.env</c> file.</param>
    public static AwykenDatabaseSettings Load(
        Func<string, string?>? hostConfiguration = null,
        IReadOnlyDictionary<string, string>? extraValues = null,
        params string[]? additionalRoots)
    {
        var fileValues = DotEnv.Load(out var dotEnvFile, additionalRoots);

        string? Get(string key)
        {
            var fromHost = hostConfiguration?.Invoke(key);
            if (!string.IsNullOrWhiteSpace(fromHost))
                return fromHost.Trim();

            if (fileValues.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile))
                return fromFile.Trim();

            if (extraValues is not null
                && extraValues.TryGetValue(key, out var fromExtra)
                && !string.IsNullOrWhiteSpace(fromExtra))
                return fromExtra.Trim();

            var fromEnvironment = Environment.GetEnvironmentVariable(key);
            return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
        }

        var connectionString = Get(ConnectionStringKey);
        var builder = string.IsNullOrWhiteSpace(connectionString)
            ? BuildFromDiscreteKeys(Get, dotEnvFile)
            : ParseConnectionString(connectionString, dotEnvFile);

        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
            throw Missing(dotEnvFile, "the connection string does not include a database name (Initial Catalog)");

        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw Missing(dotEnvFile, "the connection string does not include a server name (Data Source)");

        // The discrete-key path applies these; an explicit connection string owns its own values.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (ReadSeconds(Get(ConnectTimeoutKey), ConnectTimeoutKey) is int connectTimeout)
                builder.ConnectTimeout = connectTimeout;
        }

        var defaultAdminEmail = Get(DefaultAdminEmailKey)?.ToLowerInvariant();
        var defaultAdminPassword = Get(DefaultAdminPasswordKey);

        // A half-configured shared admin is a security trap: fail instead of guessing.
        if (!string.IsNullOrWhiteSpace(defaultAdminEmail) != !string.IsNullOrWhiteSpace(defaultAdminPassword))
        {
            throw new AwykenDatabaseConfigurationException(
                $"{DefaultAdminEmailKey} and {DefaultAdminPasswordKey} must be set together " +
                "(or both left empty). A default admin with an email but no password would never " +
                "be able to sign in.");
        }

        if (!string.IsNullOrWhiteSpace(defaultAdminPassword) && defaultAdminPassword.Length < 8)
        {
            throw new AwykenDatabaseConfigurationException(
                $"{DefaultAdminPasswordKey} must be at least 8 characters, like any other account password.");
        }

        return new AwykenDatabaseSettings
        {
            ConnectionString = builder.ConnectionString,
            DataSource = builder.DataSource,
            InitialCatalog = builder.InitialCatalog,
            CommandTimeoutSeconds = ReadSeconds(Get(CommandTimeoutKey), CommandTimeoutKey) ?? 60,
            ApplyMigrationsOnStartup = ReadBool(Get(ApplyMigrationsKey), ApplyMigrationsKey) ?? true,
            AdminEmails = ReadEmailList(Get(AdminEmailsKey)),
            DefaultAdminEmail = defaultAdminEmail,
            DefaultAdminPassword = defaultAdminPassword,
            DefaultAdminName = Get(DefaultAdminNameKey),
            DotEnvFile = dotEnvFile
        };
    }

    private static IReadOnlyList<string> ReadEmailList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(email => email.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SqlConnectionStringBuilder BuildFromDiscreteKeys(
        Func<string, string?> get, string? dotEnvFile)
    {
        var server = get(ServerKey);
        var database = get(DatabaseKey);
        var user = get(UserKey);
        var password = get(PasswordKey);
        var authentication = get(AuthenticationKey);

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
            throw Missing(dotEnvFile, null);

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            ApplicationName = "AWYKEN",
            Encrypt = (ReadBool(get(EncryptKey), EncryptKey) ?? true)
                ? SqlConnectionEncryptOption.Mandatory
                : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = ReadBool(get(TrustServerCertificateKey), TrustServerCertificateKey) ?? false
        };

        if (!string.IsNullOrWhiteSpace(authentication))
        {
            // Microsoft Entra ID sign-in: the login can be a client id, and a password is optional.
            builder.Authentication = ParseAuthentication(authentication);
            if (!string.IsNullOrWhiteSpace(user))
                builder.UserID = user;
            if (!string.IsNullOrWhiteSpace(password))
                builder.Password = password;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
                throw Missing(dotEnvFile, $"{UserKey} and {PasswordKey} are empty");

            builder.UserID = user;
            builder.Password = password;
        }

        return builder;
    }

    private static SqlConnectionStringBuilder ParseConnectionString(string connectionString, string? dotEnvFile)
    {
        try
        {
            return new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new AwykenDatabaseConfigurationException(
                $"{ConnectionStringKey} is not a valid SQL Server connection string: {exception.Message}", exception);
        }
    }

    private static SqlAuthenticationMethod ParseAuthentication(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (normalized.Length > 0
            && Enum.TryParse<SqlAuthenticationMethod>(normalized, ignoreCase: true, out var method)
            && method != SqlAuthenticationMethod.NotSpecified)
        {
            return method;
        }

        throw new AwykenDatabaseConfigurationException(
            $"{AuthenticationKey} value '{value}' is not a supported Microsoft.Data.SqlClient sign-in mode. " +
            "Use one of: Sql Password, Active Directory Password, Active Directory Integrated, " +
            "Active Directory Interactive, Active Directory Service Principal, " +
            "Active Directory Managed Identity, Active Directory Default.");
    }

    private static bool? ReadBool(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (bool.TryParse(value, out var parsed))
            return parsed;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "yes" or "y" or "on" => true,
            "0" or "no" or "n" or "off" => false,
            _ => throw new AwykenDatabaseConfigurationException(
                $"{key} must be True or False, but was '{value}'.")
        };
    }

    private static int? ReadSeconds(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            return seconds;

        throw new AwykenDatabaseConfigurationException(
            $"{key} must be a whole number of seconds greater than zero, but was '{value}'.");
    }

    private static AwykenDatabaseConfigurationException Missing(string? dotEnvFile, string? detail)
    {
        var reason = detail is null ? "" : $"\nReason: {detail}.\n";
        var location = dotEnvFile is null
            ? "No .env file was found. Copy .env.example to .env in the repository root."
            : $"Read {dotEnvFile}, but it has no usable Azure SQL values.";

        return new AwykenDatabaseConfigurationException(
            "AWYKEN could not resolve the Azure SQL Database connection." + reason +
            "\n" + location +
            "\n\nSet either a full connection string:\n" +
            $"  {ConnectionStringKey}=Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;User ID=<user>;Password=<password>;Encrypt=True;\n" +
            "\nor the discrete keys:\n" +
            $"  {ServerKey}=<server>.database.windows.net\n" +
            $"  {DatabaseKey}=<database>\n" +
            $"  {UserKey}=<user>\n" +
            $"  {PasswordKey}=<password>\n" +
            "\nThe same names work as environment variables or user secrets. " +
            "See docs/azure-sql-setup.md for the step-by-step setup.");
    }
}
